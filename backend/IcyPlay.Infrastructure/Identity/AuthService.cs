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

    public async Task<AuthResult<RegistrationResponse>> RegisterFacilityOwnerAsync(RegisterFacilityOwnerRequest request, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await CreateUserAsync(request.Email, request.FullName, request.BillingPhone, request.Password, UserRoleName.FacilityOwner, ct);
        if (user is null)
        {
            return AuthResult<RegistrationResponse>.Fail(AuthFailure.DuplicateEmail);
        }

        var profile = new FacilityOwner(user.Id, request.BusinessName, request.BillingEmail, request.BillingPhone);
        db.FacilityOwners.Add(profile);
        await db.SaveChangesAsync(ct);
        await emailVerificationService.SendAsync(
            user.Id,
            user.Email,
            user.FullName,
            ct);
        await transaction.CommitAsync(ct);
        logger.LogInformation("Facility owner account registered. UserId: {UserId}", user.Id);
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

    public async Task<AuthResult<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
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
        var response = IssueTokens(user, now);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Login succeeded. UserId: {UserId}", user.Id);
        return AuthResult<TokenResponse>.Success(response);
    }

    public async Task<AuthResult<TokenResponse>> RefreshAsync(string rawToken, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var hash = Hash(rawToken);
        var stored = await db.RefreshTokens.Include(x => x.User).ThenInclude(x => x.Roles).SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (stored is null || !stored.IsActive(now) || !stored.User.IsActive)
        {
            return AuthResult<TokenResponse>.Fail(AuthFailure.InvalidRefreshToken);
        }

        var replacementRaw = GenerateRefreshToken();
        var replacementHash = Hash(replacementRaw);
        stored.Revoke(now, replacementHash);
        db.RefreshTokens.Add(new RefreshToken(stored.UserId, replacementHash, now.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 30))));
        var response = CreateTokenResponse(stored.User, replacementRaw, now);
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

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(x => x.Roles).SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        return user is null ? null : new(user.Id, user.Email, user.FullName, user.Roles.Select(x => x.Role).ToArray());
    }

    private TokenResponse IssueTokens(User user, DateTimeOffset now)
    {
        var raw = GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken(user.Id, Hash(raw), now.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 30))));
        return CreateTokenResponse(user, raw, now);
    }
    private TokenResponse CreateTokenResponse(User user, string refreshToken, DateTimeOffset now)
    {
        var expires = now.AddMinutes(configuration.GetValue("Jwt:AccessTokenMinutes", 15));
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(JwtRegisteredClaimNames.Email, user.Email), new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, user.FullName) };
        claims.AddRange(user.Roles.Select(x => new Claim(ClaimTypes.Role, x.Role)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"] ?? throw new InvalidOperationException("JWT signing key is required.")));
        var jwt = new JwtSecurityToken(configuration["Jwt:Issuer"], configuration["Jwt:Audience"], claims, now.UtcDateTime, expires.UtcDateTime, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), refreshToken, expires, user.Roles.Select(x => x.Role).ToArray());
    }
    private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
