using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IcyPlay.Application.Email;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace IcyPlay.Infrastructure.Identity;

public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    IEmailVerificationService emailVerificationService,
    IPasswordResetEmailService passwordResetEmailService,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<AuthService> logger) : IAuthService
{
    private const int MaximumFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthResult<RegistrationResponse>> RegisterCustomerAsync(RegisterCustomerRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await CreateUserAsync(request.Email, request.FullName, request.PhoneNumber, request.Password, UserRoleName.Customer, ct);
        if (user is null)
        {
            return AuthResult<RegistrationResponse>.Fail(AuthFailure.DuplicateEmail);
        }

        var profile = new Customer(user.Id);
        db.Customers.Add(profile);
        await db.SaveChangesAsync(ct);
        await emailVerificationService.SendAsync(
            user.Id,
            user.Email,
            user.FullName,
            ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Customer account registered. UserId: {UserId}", user.Id);
        return AuthResult<RegistrationResponse>.Success(new(user.Id, profile.Id));
    }

    private async Task<User?> CreateUserAsync(string email, string fullName, string? phone, string password, string role, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == normalized, ct))
        {
            return null;
        }

        var user = new User(normalized, fullName, phone);
        user.SetPasswordHash(passwordHasher.HashPassword(user, password));
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole(user.Id, role));
        try
        {
            await db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            logger.LogWarning("Registration rejected because the normalized email already exists.");
            return null;
        }
    }

    public async Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.Include(x => x.Roles).SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null)
        {
            logger.LogWarning("Login rejected because the supplied credentials are invalid.");
            return AuthResult<TokenResponse>.Fail(AuthFailure.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login rejected for inactive account. UserId: {UserId}", user.Id);
            return AuthResult<TokenResponse>.Fail(AuthFailure.InactiveAccount);
        }

        if (user.LockoutEnd > now)
        {
            logger.LogWarning("Login rejected for locked account. UserId: {UserId}", user.Id);
            return AuthResult<TokenResponse>.Fail(AuthFailure.AccountLocked, (int)Math.Ceiling((user.LockoutEnd.Value - now).TotalSeconds));
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        {
            user.RecordFailedLogin(MaximumFailedAttempts, LockoutDuration, now);
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "Login failed. UserId: {UserId}, FailedAttempts: {FailedAttempts}, IsLocked: {IsLocked}",
                user.Id,
                user.FailedLoginAttempts,
                user.LockoutEnd > now);
            return user.LockoutEnd > now
                ? AuthResult<TokenResponse>.Fail(AuthFailure.AccountLocked, (int)LockoutDuration.TotalSeconds)
                : AuthResult<TokenResponse>.Fail(AuthFailure.InvalidCredentials);
        }
        user.RecordSuccessfulLogin(now);
        var response = IssueTokens(user, now, request.RememberMe, client);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Login succeeded. UserId: {UserId}", user.Id);
        return AuthResult<TokenResponse>.Success(response);
    }

    public async Task<AuthResult<TokenResponse>> RefreshAsync(string rawToken, ClientInfo client, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = Hash(rawToken);
        var stored = await db.RefreshTokens.Include(x => x.User).ThenInclude(x => x.Roles).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive(now) || !stored.User.IsActive)
        {
            return AuthResult<TokenResponse>.Fail(AuthFailure.InvalidRefreshToken);
        }

        var replacementRaw = GenerateRefreshToken();
        var replacementHash = Hash(replacementRaw);
        stored.Revoke(now, replacementHash);
        // Carry the choice across the rotation, otherwise every refresh would
        // quietly promote a short session to a remembered one.
        var replacement = new RefreshToken(
            stored.UserId,
            replacementHash,
            now.Add(RefreshTokenLifetime(stored.IsPersistent)),
            now,
            stored.IsPersistent,
            Truncate(client.UserAgent, 512) ?? stored.UserAgent,
            Truncate(client.IpAddress, 45) ?? stored.IpAddress);
        replacement.RecordUse(now);
        db.RefreshTokens.Add(replacement);
        var response = CreateTokenResponse(stored.User, replacementRaw, now, replacement.Id);
        await db.SaveChangesAsync(ct);
        return AuthResult<TokenResponse>.Success(response);
    }

    public async Task<AuthResult<bool>> LogoutAsync(string rawToken, CancellationToken ct)
    {
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == Hash(rawToken), ct);
        var now = timeProvider.GetUtcNow();
        if (stored is null || !stored.IsActive(now))
        {
            return AuthResult<bool>.Fail(AuthFailure.InvalidRefreshToken);
        }

        stored.Revoke(now);
        await db.SaveChangesAsync(ct);
        return AuthResult<bool>.Success(true);
    }

    public async Task<AuthResult<ResendVerificationEmailResponse>> ResendVerificationEmailAsync(
        string email,
        CancellationToken ct)
    {
        const string acceptedMessage =
            "If an account exists for this email, a verification message will be sent.";
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == normalizedEmail && candidate.IsActive,
            ct);
        if (user is null || user.IsEmailVerified)
        {
            logger.LogInformation(
                "Verification email resend accepted without disclosing account state.");
            return AuthResult<ResendVerificationEmailResponse>.Success(new(acceptedMessage));
        }

        var now = timeProvider.GetUtcNow();
        var cooldownSeconds = configuration.GetValue(
            "EmailVerification:ResendCooldownSeconds",
            60);
        var latestToken = await db.EmailVerificationTokens
            .Where(token => token.UserId == user.Id)
            .OrderByDescending(token => token.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var retryAfter = latestToken is null
            ? TimeSpan.Zero
            : latestToken.CreatedAt.AddSeconds(cooldownSeconds) - now;
        if (retryAfter > TimeSpan.Zero)
        {
            return AuthResult<ResendVerificationEmailResponse>.Fail(
                AuthFailure.VerificationCooldown,
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existingTokens = await db.EmailVerificationTokens
            .Where(token => token.UserId == user.Id)
            .ToListAsync(ct);
        db.EmailVerificationTokens.RemoveRange(existingTokens);
        await emailVerificationService.SendAsync(
            user.Id,
            user.Email,
            user.FullName,
            ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Account verification email was re-sent. UserId: {UserId}",
            user.Id);
        return AuthResult<ResendVerificationEmailResponse>.Success(new(acceptedMessage));
    }

    public async Task<AuthResult<VerifyEmailResponse>> VerifyEmailAsync(string token, CancellationToken ct)
    {
        var tokenHash = Hash(token.Trim());
        var stored = await db.EmailVerificationTokens
            .Include(entry => entry.User)
            .SingleOrDefaultAsync(entry => entry.TokenHash == tokenHash, ct);
        if (stored is null)
        {
            logger.LogWarning("Email verification failed because the token was not recognized.");
            return AuthResult<VerifyEmailResponse>.Fail(AuthFailure.InvalidVerificationToken);
        }

        var now = timeProvider.GetUtcNow();
        var user = stored.User;
        if (stored.UsedAt is not null)
        {
            // Opening the same link twice is not an error once the account is verified.
            return user.EmailVerifiedAt is DateTimeOffset verifiedAt
                ? AuthResult<VerifyEmailResponse>.Success(new(user.Email, verifiedAt, true))
                : AuthResult<VerifyEmailResponse>.Fail(AuthFailure.InvalidVerificationToken);
        }

        if (stored.ExpiresAt <= now)
        {
            logger.LogInformation(
                "Email verification failed because the token had expired. UserId: {UserId}",
                user.Id);
            return AuthResult<VerifyEmailResponse>.Fail(AuthFailure.ExpiredVerificationToken);
        }

        if (!user.IsActive)
        {
            return AuthResult<VerifyEmailResponse>.Fail(AuthFailure.InactiveAccount);
        }

        stored.MarkUsed(now);
        user.MarkEmailVerified(now);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Email address was verified. UserId: {UserId}", user.Id);
        return AuthResult<VerifyEmailResponse>.Success(new(user.Email, now, false));
    }

    public async Task<AuthResult<ForgotPasswordResponse>> ForgotPasswordAsync(
        string email,
        CancellationToken ct)
    {
        const string acceptedMessage =
            "If an account exists for this email, a password reset link will be sent.";
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == normalizedEmail && candidate.IsActive,
            ct);
        if (user is null)
        {
            logger.LogInformation(
                "Password reset request accepted without disclosing account existence.");
            return AuthResult<ForgotPasswordResponse>.Success(new(acceptedMessage));
        }

        var now = timeProvider.GetUtcNow();
        var cooldownSeconds = configuration.GetValue(
            "PasswordReset:RequestCooldownSeconds",
            60);
        var latestToken = await db.PasswordResetTokens
            .Where(token => token.UserId == user.Id)
            .OrderByDescending(token => token.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var retryAfter = latestToken is null
            ? TimeSpan.Zero
            : latestToken.CreatedAt.AddSeconds(cooldownSeconds) - now;
        if (retryAfter > TimeSpan.Zero)
        {
            return AuthResult<ForgotPasswordResponse>.Fail(
                AuthFailure.PasswordResetCooldown,
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var existingTokens = await db.PasswordResetTokens
            .Where(token => token.UserId == user.Id)
            .ToListAsync(ct);
        db.PasswordResetTokens.RemoveRange(existingTokens);
        await passwordResetEmailService.SendAsync(
            user.Id,
            user.Email,
            user.FullName,
            ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Password reset email was sent. UserId: {UserId}",
            user.Id);
        return AuthResult<ForgotPasswordResponse>.Success(new(acceptedMessage));
    }

    public async Task<AuthResult<PasswordResetTokenStatusResponse>> CheckPasswordResetTokenAsync(
        string token,
        CancellationToken ct)
    {
        var (stored, failure) = await FindUsablePasswordResetTokenAsync(token, ct);
        return failure is AuthFailure.None
            ? AuthResult<PasswordResetTokenStatusResponse>.Success(new(stored!.ExpiresAt))
            : AuthResult<PasswordResetTokenStatusResponse>.Fail(failure);
    }

    public async Task<AuthResult<ResetPasswordResponse>> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken ct)
    {
        var (stored, failure) = await FindUsablePasswordResetTokenAsync(token, ct);
        if (failure is not AuthFailure.None)
        {
            return AuthResult<ResetPasswordResponse>.Fail(failure);
        }

        var now = timeProvider.GetUtcNow();
        var user = stored!.User;

        // Only an exact match can be detected: the stored value is a one-way hash,
        // so the old password cannot be read back and compared for similarity.
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, newPassword)
            != PasswordVerificationResult.Failed)
        {
            logger.LogInformation(
                "Password reset was rejected because the new password matched the current one. UserId: {UserId}",
                user.Id);

            // The token stays unused so the same link still works on the next try.
            return AuthResult<ResetPasswordResponse>.Fail(AuthFailure.PasswordReused);
        }

        user.SetPasswordHash(passwordHasher.HashPassword(user, newPassword));
        user.ClearLockout(now);
        stored.MarkUsed(now);

        // A reset ends every existing session, so a stolen refresh token dies with
        // the old password.
        var activeRefreshTokens = await db.RefreshTokens
            .Where(refreshToken => refreshToken.UserId == user.Id && refreshToken.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.Revoke(now);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Password was reset and {RevokedSessionCount} session(s) were revoked. UserId: {UserId}",
            activeRefreshTokens.Count,
            user.Id);
        return AuthResult<ResetPasswordResponse>.Success(
            new(user.Email, activeRefreshTokens.Count));
    }

    public async Task<IReadOnlyCollection<ActiveSessionResponse>> GetActiveSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var sessions = await db.RefreshTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId && token.RevokedAt == null && token.ExpiresAt > now)
            .OrderByDescending(token => token.LastUsedAt ?? token.CreatedAt)
            .ToListAsync(ct);

        return sessions
            .Select(token => new ActiveSessionResponse(
                token.Id,
                token.UserAgent,
                token.IpAddress,
                token.CreatedAt,
                token.LastUsedAt,
                token.ExpiresAt,
                token.IsPersistent,
                token.Id == currentSessionId))
            .ToArray();
    }

    public async Task<bool> RevokeSessionAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        // Filtering on UserId as well as the id is what stops one account from
        // revoking another account's session by guessing a GUID.
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(
            token => token.Id == sessionId && token.UserId == userId && token.RevokedAt == null,
            ct);
        if (stored is null)
        {
            return false;
        }

        stored.Revoke(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "A session was revoked. UserId: {UserId}, SessionId: {SessionId}",
            userId,
            sessionId);
        return true;
    }

    public async Task<int> RevokeOtherSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var others = await db.RefreshTokens
            .Where(token =>
                token.UserId == userId &&
                token.RevokedAt == null &&
                token.Id != currentSessionId)
            .ToListAsync(ct);
        foreach (var token in others)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "{RevokedSessionCount} other session(s) were revoked. UserId: {UserId}",
            others.Count,
            userId);
        return others.Count;
    }

    /// <summary>
    /// Ends every session including the caller's. Kept separate from
    /// RevokeOtherSessionsAsync rather than passing a null id: comparing a
    /// non-nullable column to NULL is UNKNOWN in SQL, which would silently
    /// revoke nothing.
    /// </summary>
    public async Task<int> RevokeAllSessionsAsync(Guid userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var sessions = await db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in sessions)
        {
            token.Revoke(now);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "All {RevokedSessionCount} session(s) were revoked. UserId: {UserId}",
            sessions.Count,
            userId);
        return sessions.Count;
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        return user is null ? null : new(user.Id, user.Email, user.FullName, user.Roles.Select(x => x.Role).ToArray());
    }

    private TokenResponse IssueTokens(User user, DateTimeOffset now, bool rememberMe, ClientInfo client)
    {
        var raw = GenerateRefreshToken();
        var refreshToken = new RefreshToken(
            user.Id,
            Hash(raw),
            now.Add(RefreshTokenLifetime(rememberMe)),
            now,
            rememberMe,
            Truncate(client.UserAgent, 512),
            Truncate(client.IpAddress, 45));
        db.RefreshTokens.Add(refreshToken);
        return CreateTokenResponse(user, raw, now, refreshToken.Id);
    }

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength ? value : value[..maximumLength];

    /// <summary>
    /// Without "remember me" the session is deliberately short, so a token
    /// taken from a shared computer stops working within the day rather than
    /// in a month.
    /// </summary>
    private TimeSpan RefreshTokenLifetime(bool rememberMe) => rememberMe
        ? TimeSpan.FromDays(configuration.GetValue("Jwt:RefreshTokenDays", 30))
        : TimeSpan.FromHours(configuration.GetValue("Jwt:SessionRefreshTokenHours", 12));
    private TokenResponse CreateTokenResponse(User user, string refreshToken, DateTimeOffset now, Guid sessionId)
    {
        var expires = now.AddMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", 15));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            // Lets an authenticated request say which session it belongs to
            // without ever sending the refresh token back.
            new(JwtRegisteredClaimNames.Sid, sessionId.ToString())
        };
        claims.AddRange(user.Roles.Select(x => new Claim(ClaimTypes.Role, x.Role)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("JWT signing key is required.")));
        var jwt = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, now.UtcDateTime, expires.UtcDateTime, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), refreshToken, expires, user.Roles.Select(x => x.Role).ToArray());
    }
    /// <summary>
    /// Resolves a password reset token that is present, unused, unexpired and
    /// owned by an active account. Used by both the pre-flight check and the
    /// reset itself so the two can never disagree.
    /// </summary>
    private async Task<(PasswordResetToken? Token, AuthFailure Failure)> FindUsablePasswordResetTokenAsync(
        string token,
        CancellationToken ct)
    {
        var tokenHash = Hash(token.Trim());
        var stored = await db.PasswordResetTokens
            .Include(entry => entry.User)
            .SingleOrDefaultAsync(entry => entry.TokenHash == tokenHash, ct);
        if (stored is null)
        {
            logger.LogWarning("A password reset token was not recognized.");
            return (null, AuthFailure.InvalidPasswordResetToken);
        }

        if (stored.UsedAt is not null)
        {
            logger.LogInformation(
                "A password reset token was already used. UserId: {UserId}",
                stored.UserId);
            return (null, AuthFailure.InvalidPasswordResetToken);
        }

        if (stored.ExpiresAt <= timeProvider.GetUtcNow())
        {
            logger.LogInformation(
                "A password reset token had expired. UserId: {UserId}",
                stored.UserId);
            return (null, AuthFailure.ExpiredPasswordResetToken);
        }

        return stored.User.IsActive
            ? (stored, AuthFailure.None)
            : (null, AuthFailure.InactiveAccount);
    }

    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
