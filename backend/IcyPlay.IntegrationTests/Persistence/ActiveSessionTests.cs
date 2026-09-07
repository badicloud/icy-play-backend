using FluentAssertions.Execution;
using IcyPlay.Application.Email;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class ActiveSessionTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset SignInTime =
        new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private const string Password = "SessionPassword123!";

    private static readonly ClientInfo Phone =
        new("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) Safari/605.1", "112.201.5.10");

    private static readonly ClientInfo Laptop =
        new("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/128.0 Safari/537.36", "112.201.5.11");

    [Fact]
    public async Task LoginAsync_ShouldRecordTheDeviceBehindTheSession()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-device@example.com");
        var sut = CreateAuthService(context);

        // Act
        await sut.LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        context.ChangeTracker.Clear();
        var stored = await context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == user.Id);

        // Assert
        using (new AssertionScope())
        {
            stored.UserAgent.Should().Be(Phone.UserAgent);
            stored.IpAddress.Should().Be(Phone.IpAddress);
            stored.CreatedAt.Should().Be(SignInTime);
            stored.LastUsedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetActiveSessionsAsync_ShouldListEverySessionAndFlagTheCaller()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-list@example.com");
        var sut = CreateAuthService(context);
        await sut.LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        var laptopLogin = await CreateAuthService(context, SignInTime.AddMinutes(5))
            .LoginAsync(LoginFor(user), Laptop, CancellationToken.None);
        var currentSessionId = await SessionIdForAsync(context, laptopLogin.Value!.RefreshToken);

        // Act
        var sessions = await sut.GetActiveSessionsAsync(
            user.Id,
            currentSessionId,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            sessions.Should().HaveCount(2);
            sessions.Should().ContainSingle(session => session.IsCurrent);
            sessions.Single(session => session.IsCurrent).UserAgent.Should().Be(Laptop.UserAgent);
            sessions.Single(session => !session.IsCurrent).IpAddress.Should().Be(Phone.IpAddress);
        }
    }

    [Fact]
    public async Task GetActiveSessionsAsync_ShouldLeaveOutRevokedAndExpiredSessions()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-filtered@example.com");
        var live = await CreateAuthService(context)
            .LoginAsync(LoginFor(user), Laptop, CancellationToken.None);
        var revoked = await CreateAuthService(context, SignInTime.AddMinutes(1))
            .LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        await CreateAuthService(context, SignInTime.AddMinutes(2))
            .RevokeSessionAsync(
                user.Id,
                await SessionIdForAsync(context, revoked.Value!.RefreshToken),
                CancellationToken.None);

        // Act: thirteen hours on, the unremembered twelve-hour sessions have lapsed.
        var sessions = await CreateAuthService(context, SignInTime.AddHours(13))
            .GetActiveSessionsAsync(user.Id, null, CancellationToken.None);
        var stillActive = await CreateAuthService(context, SignInTime.AddMinutes(30))
            .GetActiveSessionsAsync(user.Id, null, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            sessions.Should().BeEmpty();
            stillActive.Should().ContainSingle();
            stillActive.Single().Id.Should().Be(
                await SessionIdForAsync(context, live.Value!.RefreshToken));
        }
    }

    [Fact]
    public async Task RevokeSessionAsync_ShouldEndTheSessionAndStopItsRefreshToken()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-revoke@example.com");
        var login = await CreateAuthService(context)
            .LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        var sessionId = await SessionIdForAsync(context, login.Value!.RefreshToken);
        var revokeTime = SignInTime.AddMinutes(10);
        var sut = CreateAuthService(context, revokeTime);

        // Act
        var revoked = await sut.RevokeSessionAsync(user.Id, sessionId, CancellationToken.None);
        var refreshAttempt = await sut.RefreshAsync(
            login.Value.RefreshToken,
            Phone,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            revoked.Should().BeTrue();
            refreshAttempt.Succeeded.Should().BeFalse();
            refreshAttempt.Failure.Should().Be(AuthFailure.InvalidRefreshToken);
            (await sut.GetActiveSessionsAsync(user.Id, null, CancellationToken.None))
                .Should().BeEmpty();
        }
    }

    [Fact]
    public async Task RevokeSessionAsync_WhenSessionBelongsToAnotherAccount_ShouldRefuseAndLeaveItAlive()
    {
        // Arrange
        await using var context = database.CreateContext();
        var owner = await AddUserAsync(context, "session-owner@example.com");
        var attacker = await AddUserAsync(context, "session-attacker@example.com");
        var ownerLogin = await CreateAuthService(context)
            .LoginAsync(LoginFor(owner), Phone, CancellationToken.None);
        var ownerSessionId = await SessionIdForAsync(context, ownerLogin.Value!.RefreshToken);
        var sut = CreateAuthService(context, SignInTime.AddMinutes(5));

        // Act
        var revoked = await sut.RevokeSessionAsync(
            attacker.Id,
            ownerSessionId,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            revoked.Should().BeFalse();
            (await sut.GetActiveSessionsAsync(owner.Id, null, CancellationToken.None))
                .Should().ContainSingle();
        }
    }

    [Fact]
    public async Task RevokeOtherSessionsAsync_ShouldClearTheRestAndKeepTheCallerSignedIn()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-revoke-others@example.com");
        await CreateAuthService(context).LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        await CreateAuthService(context, SignInTime.AddMinutes(1))
            .LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        var keptLogin = await CreateAuthService(context, SignInTime.AddMinutes(2))
            .LoginAsync(LoginFor(user), Laptop, CancellationToken.None);
        var keptSessionId = await SessionIdForAsync(context, keptLogin.Value!.RefreshToken);
        var sut = CreateAuthService(context, SignInTime.AddMinutes(5));

        // Act
        var revokedCount = await sut.RevokeOtherSessionsAsync(
            user.Id,
            keptSessionId,
            CancellationToken.None);
        var remaining = await sut.GetActiveSessionsAsync(
            user.Id,
            keptSessionId,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            revokedCount.Should().Be(2);
            remaining.Should().ContainSingle();
            remaining.Single().Id.Should().Be(keptSessionId);
            remaining.Single().IsCurrent.Should().BeTrue();
        }
    }

    [Fact]
    public async Task RefreshAsync_ShouldCarryTheDeviceForwardAndStampLastUsed()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-refresh@example.com");
        var login = await CreateAuthService(context)
            .LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        var refreshTime = SignInTime.AddMinutes(20);

        // Act
        var refreshed = await CreateAuthService(context, refreshTime)
            .RefreshAsync(login.Value!.RefreshToken, Phone, CancellationToken.None);
        context.ChangeTracker.Clear();
        var rotated = await context.RefreshTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == user.Id && token.RevokedAt == null);

        // Assert
        using (new AssertionScope())
        {
            refreshed.Succeeded.Should().BeTrue();
            rotated.UserAgent.Should().Be(Phone.UserAgent);
            rotated.IpAddress.Should().Be(Phone.IpAddress);
            rotated.LastUsedAt.Should().Be(refreshTime);
        }
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_ShouldEndEverySessionIncludingTheCaller()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "session-revoke-all@example.com");
        var first = await CreateAuthService(context)
            .LoginAsync(LoginFor(user), Phone, CancellationToken.None);
        await CreateAuthService(context, SignInTime.AddMinutes(1))
            .LoginAsync(LoginFor(user), Laptop, CancellationToken.None);
        var sut = CreateAuthService(context, SignInTime.AddMinutes(5));

        // Act
        var revokedCount = await sut.RevokeAllSessionsAsync(user.Id, CancellationToken.None);
        var remaining = await sut.GetActiveSessionsAsync(user.Id, null, CancellationToken.None);
        var refreshAttempt = await sut.RefreshAsync(
            first.Value!.RefreshToken,
            Phone,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            revokedCount.Should().Be(2);
            remaining.Should().BeEmpty();
            refreshAttempt.Succeeded.Should().BeFalse();
        }
    }

    private static LoginRequest LoginFor(User user) =>
        new(user.Email, Password, "integration-test-captcha", false);

    private static async Task<Guid> SessionIdForAsync(AppDbContext context, string rawRefreshToken)
    {
        var hash = HashToken(rawRefreshToken);
        return await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == hash)
            .Select(token => token.Id)
            .SingleAsync();
    }

    private static string HashToken(string value) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value)));

    private static async Task<User> AddUserAsync(AppDbContext context, string email)
    {
        var user = new User(email, "Session Test Customer", "+639171234567");
        user.SetPasswordHash(new PasswordHasher<User>().HashPassword(user, Password));
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static AuthService CreateAuthService(AppDbContext context, DateTimeOffset? now = null)
    {
        return new AuthService(
            context,
            new PasswordHasher<User>(),
            new NoOpEmailVerificationService(),
            new NoOpPasswordResetEmailService(),
            new FixedTimeProvider(now ?? SignInTime),
            TestConfiguration(),
            NullLogger<AuthService>.Instance);
    }

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoOpEmailVerificationService : IEmailVerificationService
    {
        public Task SendAsync(
            Guid userId,
            string recipientEmail,
            string recipientName,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoOpPasswordResetEmailService : IPasswordResetEmailService
    {
        public Task SendAsync(
            Guid userId,
            string recipientEmail,
            string recipientName,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
