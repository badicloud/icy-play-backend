using System.Security.Cryptography;
using System.Text;
using FluentAssertions.Execution;
using IcyPlay.Application.Email;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class PasswordResetTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset RequestTime =
        new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

    private const string OldPassword = "OldPassword123!";
    private const string NewPassword = "BrandNewPassword456!";

    [Fact]
    public async Task ForgotPasswordAsync_WhenAccountExists_ShouldPersistTokenAndSendEmail()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-request@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        var sut = CreateAuthService(context, emailSender);

        // Act
        var result = await sut.ForgotPasswordAsync(user.Email, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedToken = await context.PasswordResetTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == user.Id);
        var rawToken = ReadTokenFromEmail(emailSender);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            emailSender.CallCount.Should().Be(1);
            emailSender.Message!.TemplateKey.Should().Be(EmailTemplateKey.PasswordReset);
            emailSender.Message.RecipientEmail.Should().Be(user.Email);
            emailSender.Message.Variables["expiration_minutes"].Should().Be(60);
            rawToken.Should().NotBeNullOrWhiteSpace();
            storedToken.TokenHash.Should().Be(Hash(rawToken));
            storedToken.TokenHash.Should().NotBe(rawToken);
            storedToken.ExpiresAt.Should().Be(RequestTime.AddMinutes(60));
            storedToken.UsedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task ForgotPasswordAsync_WhenAccountDoesNotExist_ShouldReturnGenericSuccessWithoutSending()
    {
        // Arrange
        await using var context = database.CreateContext();
        var emailSender = new CapturingTransactionalEmailSender();
        var sut = CreateAuthService(context, emailSender);

        // Act
        var result = await sut.ForgotPasswordAsync(
            "nobody-here@example.com",
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            result.Value!.Message.Should().Contain("If an account exists");
            emailSender.CallCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task ForgotPasswordAsync_WhenCooldownIsActive_ShouldReturnRetryDelayWithoutSending()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-cooldown@example.com");
        context.PasswordResetTokens.Add(new PasswordResetToken(
            user.Id,
            "existing-password-reset-token-hash",
            RequestTime.AddMinutes(60),
            RequestTime));
        await context.SaveChangesAsync();
        var emailSender = new CapturingTransactionalEmailSender();
        var retryTime = RequestTime.AddSeconds(20);
        var sut = CreateAuthService(context, emailSender, retryTime);

        // Act
        var result = await sut.ForgotPasswordAsync(user.Email, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.PasswordResetCooldown);
            result.RetryAfterSeconds.Should().Be(40);
            emailSender.CallCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task ForgotPasswordAsync_WhenCooldownHasElapsed_ShouldReplaceTheEarlierToken()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-replace@example.com");
        context.PasswordResetTokens.Add(new PasswordResetToken(
            user.Id,
            "stale-password-reset-token-hash",
            RequestTime.AddMinutes(60),
            RequestTime));
        await context.SaveChangesAsync();
        var emailSender = new CapturingTransactionalEmailSender();
        var retryTime = RequestTime.AddSeconds(61);
        var sut = CreateAuthService(context, emailSender, retryTime);

        // Act
        var result = await sut.ForgotPasswordAsync(user.Email, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedTokens = await context.PasswordResetTokens
            .AsNoTracking()
            .Where(token => token.UserId == user.Id)
            .ToListAsync();

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            emailSender.CallCount.Should().Be(1);
            storedTokens.Should().ContainSingle();
            storedTokens[0].TokenHash.Should().NotBe("stale-password-reset-token-hash");
            storedTokens[0].CreatedAt.Should().Be(retryTime);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTokenIsValid_ShouldChangePasswordAndRevokeEverySession()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-success@example.com");
        context.RefreshTokens.Add(new RefreshToken(
            user.Id,
            "active-refresh-token-hash-one",
            RequestTime.AddDays(30)));
        context.RefreshTokens.Add(new RefreshToken(
            user.Id,
            "active-refresh-token-hash-two",
            RequestTime.AddDays(30)));
        await context.SaveChangesAsync();
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var resetTime = RequestTime.AddMinutes(5);
        var sut = CreateAuthService(context, emailSender, resetTime);

        // Act
        var result = await sut.ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);
        var storedToken = await context.PasswordResetTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == user.Id);
        var refreshTokens = await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.UserId == user.Id)
            .ToListAsync();
        var hasher = new PasswordHasher<User>();

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            result.Value!.Email.Should().Be(user.Email);
            result.Value.RevokedSessions.Should().Be(2);
            hasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, NewPassword)
                .Should().NotBe(PasswordVerificationResult.Failed);
            hasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, OldPassword)
                .Should().Be(PasswordVerificationResult.Failed);
            storedToken.UsedAt.Should().Be(resetTime);
            refreshTokens.Should().OnlyContain(token => token.RevokedAt == resetTime);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenNewPasswordMatchesTheCurrentOne_ShouldRejectAndKeepTheLinkUsable()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-reuse@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var resetTime = RequestTime.AddMinutes(5);
        var sut = CreateAuthService(context, emailSender, resetTime);

        // Act
        var reuseResult = await sut.ResetPasswordAsync(rawToken, OldPassword, CancellationToken.None);
        var retryResult = await sut.ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);
        var hasher = new PasswordHasher<User>();

        // Assert
        using (new AssertionScope())
        {
            reuseResult.Succeeded.Should().BeFalse();
            reuseResult.Failure.Should().Be(AuthFailure.PasswordReused);

            // The rejected attempt must not burn the link.
            retryResult.Succeeded.Should().BeTrue();
            hasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, NewPassword)
                .Should().NotBe(PasswordVerificationResult.Failed);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenTokenHasExpired_ShouldFailAndLeavePasswordUnchanged()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-expired@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var expiredTime = RequestTime.AddMinutes(61);
        var sut = CreateAuthService(context, emailSender, expiredTime);

        // Act
        var result = await sut.ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);
        var hasher = new PasswordHasher<User>();

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.ExpiredPasswordResetToken);
            hasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, OldPassword)
                .Should().NotBe(PasswordVerificationResult.Failed);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenLinkIsOpenedTwice_ShouldRejectTheSecondAttempt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-twice@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var firstReset = RequestTime.AddMinutes(5);
        await CreateAuthService(context, emailSender, firstReset)
            .ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);
        var sut = CreateAuthService(context, emailSender, RequestTime.AddMinutes(10));

        // Act
        var result = await sut.ResetPasswordAsync(
            rawToken,
            "YetAnotherPassword789!",
            CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);
        var hasher = new PasswordHasher<User>();

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.InvalidPasswordResetToken);
            hasher.VerifyHashedPassword(storedUser, storedUser.PasswordHash, NewPassword)
                .Should().NotBe(PasswordVerificationResult.Failed);
        }
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenAccountIsLockedOut_ShouldClearTheLockout()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "reset-locked@example.com");
        for (var attempt = 0; attempt < 5; attempt++)
        {
            user.RecordFailedLogin(5, TimeSpan.FromMinutes(15), RequestTime);
        }

        await context.SaveChangesAsync();
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var resetTime = RequestTime.AddMinutes(5);
        var sut = CreateAuthService(context, emailSender, resetTime);

        // Act
        var result = await sut.ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            storedUser.FailedLoginAttempts.Should().Be(0);
            storedUser.LockoutEnd.Should().BeNull();
        }
    }

    [Fact]
    public async Task CheckPasswordResetTokenAsync_WhenTokenIsUsable_ShouldSucceedWithoutConsumingIt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "check-usable@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var checkTime = RequestTime.AddMinutes(5);
        var sut = CreateAuthService(context, emailSender, checkTime);

        // Act
        var checkResult = await sut.CheckPasswordResetTokenAsync(rawToken, CancellationToken.None);
        var resetResult = await sut.ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            checkResult.Succeeded.Should().BeTrue();
            checkResult.Value!.ExpiresAt.Should().Be(RequestTime.AddMinutes(60));

            // Checking must never burn the link.
            resetResult.Succeeded.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CheckPasswordResetTokenAsync_WhenTokenWasAlreadyUsed_ShouldReportItAsInvalid()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "check-spent@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var resetTime = RequestTime.AddMinutes(5);
        await CreateAuthService(context, emailSender, resetTime)
            .ResetPasswordAsync(rawToken, NewPassword, CancellationToken.None);
        var sut = CreateAuthService(context, emailSender, RequestTime.AddMinutes(10));

        // Act
        var result = await sut.CheckPasswordResetTokenAsync(rawToken, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.InvalidPasswordResetToken);
        }
    }

    [Fact]
    public async Task CheckPasswordResetTokenAsync_WhenTokenHasExpired_ShouldReportItAsExpired()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "check-expired@example.com");
        var emailSender = new CapturingTransactionalEmailSender();
        await CreateAuthService(context, emailSender)
            .ForgotPasswordAsync(user.Email, CancellationToken.None);
        var rawToken = ReadTokenFromEmail(emailSender);
        var sut = CreateAuthService(context, emailSender, RequestTime.AddMinutes(61));

        // Act
        var result = await sut.CheckPasswordResetTokenAsync(rawToken, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.ExpiredPasswordResetToken);
        }
    }

    [Fact]
    public async Task CheckPasswordResetTokenAsync_WhenTokenIsUnknown_ShouldReportItAsInvalid()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateAuthService(context, new CapturingTransactionalEmailSender());

        // Act
        var result = await sut.CheckPasswordResetTokenAsync(
            "a-token-that-was-never-issued",
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(AuthFailure.InvalidPasswordResetToken);
    }

    [Theory]
    [InlineData(true, 30 * 24)]
    [InlineData(false, 12)]
    public async Task LoginAsync_ShouldSizeTheRefreshTokenToTheRememberMeChoice(
        bool rememberMe,
        int expectedLifetimeHours)
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, $"remember-{rememberMe}@example.com");
        var sut = CreateAuthService(context, new CapturingTransactionalEmailSender());

        // Act
        var result = await sut.LoginAsync(
            new LoginRequest(user.Email, OldPassword, "integration-test-captcha", rememberMe),
            ClientInfo.Unknown,
            CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedToken = await context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == user.Id);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            storedToken.ExpiresAt.Should().Be(RequestTime.AddHours(expectedLifetimeHours));
        }
    }

    [Fact]
    public async Task RefreshAsync_WhenSessionWasNotRemembered_ShouldNotExtendItToARememberedLifetime()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "remember-rotation@example.com");
        var loginResult = await CreateAuthService(context, new CapturingTransactionalEmailSender())
            .LoginAsync(
                new LoginRequest(user.Email, OldPassword, "integration-test-captcha", false),
                ClientInfo.Unknown,
                CancellationToken.None);
        var refreshTime = RequestTime.AddHours(2);
        var sut = CreateAuthService(context, new CapturingTransactionalEmailSender(), refreshTime);

        // Act
        var result = await sut.RefreshAsync(
            loginResult.Value!.RefreshToken,
            ClientInfo.Unknown,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();

            // Twelve hours from the refresh, not thirty days.
            result.Value!.RefreshToken.Should().NotBe(loginResult.Value.RefreshToken);
            context.ChangeTracker.Clear();
            var rotated = await context.RefreshTokens
                .AsNoTracking()
                .Where(token => token.UserId == user.Id && token.RevokedAt == null)
                .SingleAsync();
            rotated.ExpiresAt.Should().Be(refreshTime.AddHours(12));
        }
    }

    private static async Task<User> AddUserAsync(AppDbContext context, string email)
    {
        var user = new User(email, "Password Reset Customer", "+639171234567");
        user.SetPasswordHash(new PasswordHasher<User>().HashPassword(user, OldPassword));
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static AuthService CreateAuthService(
        AppDbContext context,
        ITransactionalEmailSender emailSender,
        DateTimeOffset? now = null)
    {
        var timeProvider = new FixedTimeProvider(now ?? RequestTime);
        return new AuthService(
            context,
            new PasswordHasher<User>(),
            new NoOpEmailVerificationService(),
            new PasswordResetEmailService(
                context,
                emailSender,
                Options.Create(new PasswordResetOptions
                {
                    ResetUrl = "https://icyplay.example.com/reset-password",
                    ExpirationMinutes = 60,
                    RequestCooldownSeconds = 60,
                    SupportEmail = "support@icyplay.example.com"
                }),
                timeProvider,
                NullLogger<PasswordResetEmailService>.Instance),
            timeProvider,
            TestConfiguration(),
            NullLogger<AuthService>.Instance);
    }

    /// <summary>
    /// Login issues JWTs, so these tests need real token settings rather than
    /// the empty configuration the password-reset paths could get away with.
    /// </summary>
    private static IConfiguration TestConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "IcyPlay.Tests",
                ["Jwt:Audience"] = "IcyPlay.Tests.Clients",
                ["Jwt:SigningKey"] = "integration-test-signing-key-long-enough-for-hmac-sha256",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "30",
                ["Jwt:SessionRefreshTokenHours"] = "12"
            })
            .Build();
    }

    private static string ReadTokenFromEmail(CapturingTransactionalEmailSender emailSender)
    {
        var resetUrl = emailSender.Message!.Variables["reset_url"].ToString()!;
        return QueryHelpers.ParseQuery(new Uri(resetUrl).Query)["token"].ToString();
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingTransactionalEmailSender : ITransactionalEmailSender
    {
        public int CallCount
        {
            get; private set;
        }
        public TransactionalEmailMessage? Message
        {
            get; private set;
        }

        public Task SendAsync(
            TransactionalEmailMessage message,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Message = message;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpEmailVerificationService : IEmailVerificationService
    {
        public Task SendAsync(
            Guid userId,
            string recipientEmail,
            string recipientName,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
