using System.Security.Cryptography;
using System.Text;
using FluentAssertions.Execution;
using IcyPlay.Application.Email;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class CustomerRegistrationEmailVerificationTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset RegistrationTime =
        new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterCustomerAsync_WhenRegistrationSucceeds_ShouldPersistTokenAndSendVerificationEmail()
    {
        // Arrange
        await using var context = database.CreateContext();
        var emailSender = new CapturingTransactionalEmailSender();
        var emailVerificationService = CreateEmailVerificationService(context, emailSender);
        var sut = CreateAuthService(context, emailVerificationService);
        var request = CreateRequest("verification-success@example.com");

        // Act
        var result = await sut.RegisterCustomerAsync(request, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedToken = await context.EmailVerificationTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == result.Value!.UserId);
        var verificationUrl = emailSender.Message!.Variables["verification_url"].ToString()!;
        var rawToken = QueryHelpers.ParseQuery(new Uri(verificationUrl).Query)["token"].ToString();

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            emailSender.CallCount.Should().Be(1);
            emailSender.Message.TemplateKey.Should().Be(EmailTemplateKey.AccountVerification);
            emailSender.Message.RecipientEmail.Should().Be(request.Email);
            emailSender.Message.RecipientName.Should().Be(request.FullName);
            emailSender.Message.Variables["expiration_hours"].Should().Be(24);
            emailSender.Message.Variables["current_year"].Should().Be(2026);
            rawToken.Should().NotBeNullOrWhiteSpace();
            storedToken.TokenHash.Should().Be(Hash(rawToken));
            storedToken.TokenHash.Should().NotBe(rawToken);
            storedToken.ExpiresAt.Should().Be(RegistrationTime.AddHours(24));
        }
    }

    [Fact]
    public async Task RegisterCustomerAsync_WhenEmailDeliveryFails_ShouldRollbackRegistration()
    {
        // Arrange
        const string email = "verification-failure@example.com";
        await using var context = database.CreateContext();
        var emailVerificationService = CreateEmailVerificationService(
            context,
            new ThrowingTransactionalEmailSender());
        var sut = CreateAuthService(context, emailVerificationService);
        var request = CreateRequest(email);

        // Act
        var act = () => sut.RegisterCustomerAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        context.ChangeTracker.Clear();
        using (new AssertionScope())
        {
            (await context.Users.CountAsync(user => user.Email == email)).Should().Be(0);
            (await context.Customers.CountAsync(customer => customer.User.Email == email)).Should().Be(0);
            (await context.EmailVerificationTokens.CountAsync(token => token.User.Email == email)).Should().Be(0);
        }
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WhenCooldownHasElapsed_ShouldReplaceTokenAndSendEmail()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = new User(
            "resend-success@example.com",
            "Resend Verification Customer",
            "+639171234567");
        user.SetPasswordHash("integration-test-password-hash");
        context.Users.Add(user);
        context.EmailVerificationTokens.Add(new EmailVerificationToken(
            user.Id,
            "old-verification-token-hash",
            RegistrationTime.AddHours(24),
            RegistrationTime));
        await context.SaveChangesAsync();
        var resendTime = RegistrationTime.AddSeconds(61);
        var emailSender = new CapturingTransactionalEmailSender();
        var emailVerificationService = CreateEmailVerificationService(
            context,
            emailSender,
            resendTime);
        var sut = CreateAuthService(context, emailVerificationService, resendTime);

        // Act
        var result = await sut.ResendVerificationEmailAsync(
            user.Email,
            CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedTokens = await context.EmailVerificationTokens
            .AsNoTracking()
            .Where(token => token.UserId == user.Id)
            .ToListAsync();

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            emailSender.CallCount.Should().Be(1);
            storedTokens.Should().ContainSingle();
            storedTokens[0].TokenHash.Should().NotBe("old-verification-token-hash");
            storedTokens[0].CreatedAt.Should().Be(resendTime);
        }
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WhenCooldownIsActive_ShouldReturnRetryDelayWithoutSending()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = new User(
            "resend-cooldown@example.com",
            "Cooldown Customer",
            "+639171234567");
        user.SetPasswordHash("integration-test-password-hash");
        context.Users.Add(user);
        context.EmailVerificationTokens.Add(new EmailVerificationToken(
            user.Id,
            "cooldown-verification-token-hash",
            RegistrationTime.AddHours(24),
            RegistrationTime));
        await context.SaveChangesAsync();
        var emailSender = new CapturingTransactionalEmailSender();
        var emailVerificationService = CreateEmailVerificationService(
            context,
            emailSender,
            RegistrationTime.AddSeconds(20));
        var sut = CreateAuthService(
            context,
            emailVerificationService,
            RegistrationTime.AddSeconds(20));

        // Act
        var result = await sut.ResendVerificationEmailAsync(
            user.Email,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.VerificationCooldown);
            result.RetryAfterSeconds.Should().Be(40);
            emailSender.CallCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_WhenAccountDoesNotExist_ShouldReturnGenericSuccessWithoutSending()
    {
        // Arrange
        await using var context = database.CreateContext();
        var emailSender = new CapturingTransactionalEmailSender();
        var emailVerificationService = CreateEmailVerificationService(context, emailSender);
        var sut = CreateAuthService(context, emailVerificationService);

        // Act
        var result = await sut.ResendVerificationEmailAsync(
            "unknown-account@example.com",
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
    public async Task ResendVerificationEmailAsync_WhenAccountIsAlreadyVerified_ShouldReturnGenericSuccessWithoutSending()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = new User(
            "resend-already-verified@example.com",
            "Already Verified Customer",
            "+639171234567");
        user.SetPasswordHash("integration-test-password-hash");
        user.MarkEmailVerified(RegistrationTime);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var resendTime = RegistrationTime.AddHours(1);
        var emailSender = new CapturingTransactionalEmailSender();
        var sut = CreateAuthService(
            context,
            CreateEmailVerificationService(context, emailSender, resendTime),
            resendTime);

        // Act
        var result = await sut.ResendVerificationEmailAsync(user.Email, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            emailSender.CallCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenTokenIsValid_ShouldMarkTokenUsedAndVerifyAccount()
    {
        // Arrange
        await using var context = database.CreateContext();
        var emailSender = new CapturingTransactionalEmailSender();
        var registration = await RegisterAsync(context, emailSender, "verify-success@example.com");
        var rawToken = ReadTokenFromEmail(emailSender);
        var verificationTime = RegistrationTime.AddMinutes(5);
        var sut = CreateAuthService(
            context,
            CreateEmailVerificationService(context, emailSender, verificationTime),
            verificationTime);

        // Act
        var result = await sut.VerifyEmailAsync(rawToken, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == registration.UserId);
        var storedToken = await context.EmailVerificationTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == registration.UserId);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            result.Value!.Email.Should().Be("verify-success@example.com");
            result.Value.AlreadyVerified.Should().BeFalse();
            result.Value.VerifiedAt.Should().Be(verificationTime);
            storedUser.EmailVerifiedAt.Should().Be(verificationTime);
            storedToken.UsedAt.Should().Be(verificationTime);
        }
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenTokenIsUnknown_ShouldFailWithInvalidVerificationToken()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateAuthService(
            context,
            CreateEmailVerificationService(context, new CapturingTransactionalEmailSender()));

        // Act
        var result = await sut.VerifyEmailAsync(
            "unknown-verification-token",
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.InvalidVerificationToken);
        }
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenTokenHasExpired_ShouldFailAndLeaveAccountUnverified()
    {
        // Arrange
        await using var context = database.CreateContext();
        var emailSender = new CapturingTransactionalEmailSender();
        var registration = await RegisterAsync(context, emailSender, "verify-expired@example.com");
        var rawToken = ReadTokenFromEmail(emailSender);
        var expiredTime = RegistrationTime.AddHours(25);
        var sut = CreateAuthService(
            context,
            CreateEmailVerificationService(context, emailSender, expiredTime),
            expiredTime);

        // Act
        var result = await sut.VerifyEmailAsync(rawToken, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == registration.UserId);
        var storedToken = await context.EmailVerificationTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == registration.UserId);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AuthFailure.ExpiredVerificationToken);
            storedUser.EmailVerifiedAt.Should().BeNull();
            storedToken.UsedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task VerifyEmailAsync_WhenLinkIsOpenedTwice_ShouldReportAccountAsAlreadyVerified()
    {
        // Arrange
        await using var context = database.CreateContext();
        var emailSender = new CapturingTransactionalEmailSender();
        var registration = await RegisterAsync(context, emailSender, "verify-twice@example.com");
        var rawToken = ReadTokenFromEmail(emailSender);
        var firstVerification = RegistrationTime.AddMinutes(5);
        var secondVerification = RegistrationTime.AddMinutes(30);
        var firstSut = CreateAuthService(
            context,
            CreateEmailVerificationService(context, emailSender, firstVerification),
            firstVerification);
        await firstSut.VerifyEmailAsync(rawToken, CancellationToken.None);
        var sut = CreateAuthService(
            context,
            CreateEmailVerificationService(context, emailSender, secondVerification),
            secondVerification);

        // Act
        var result = await sut.VerifyEmailAsync(rawToken, CancellationToken.None);
        context.ChangeTracker.Clear();
        var storedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == registration.UserId);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            result.Value!.AlreadyVerified.Should().BeTrue();
            result.Value.VerifiedAt.Should().Be(firstVerification);
            storedUser.EmailVerifiedAt.Should().Be(firstVerification);
        }
    }

    private static async Task<RegistrationResponse> RegisterAsync(
        IcyPlay.Infrastructure.Persistence.AppDbContext context,
        CapturingTransactionalEmailSender emailSender,
        string email)
    {
        var registration = CreateAuthService(
            context,
            CreateEmailVerificationService(context, emailSender));
        var result = await registration.RegisterCustomerAsync(
            CreateRequest(email),
            CancellationToken.None);
        return result.Value!;
    }

    private static string ReadTokenFromEmail(CapturingTransactionalEmailSender emailSender)
    {
        var verificationUrl = emailSender.Message!.Variables["verification_url"].ToString()!;
        return QueryHelpers.ParseQuery(new Uri(verificationUrl).Query)["token"].ToString();
    }

    private static AuthService CreateAuthService(
        IcyPlay.Infrastructure.Persistence.AppDbContext context,
        IEmailVerificationService emailVerificationService,
        DateTimeOffset? now = null)
    {
        return new AuthService(
            context,
            new PasswordHasher<User>(),
            emailVerificationService,
            new FixedTimeProvider(now ?? RegistrationTime),
            new ConfigurationBuilder().Build(),
            NullLogger<AuthService>.Instance);
    }

    private static EmailVerificationService CreateEmailVerificationService(
        IcyPlay.Infrastructure.Persistence.AppDbContext context,
        ITransactionalEmailSender emailSender,
        DateTimeOffset? now = null)
    {
        return new EmailVerificationService(
            context,
            emailSender,
            Options.Create(new EmailVerificationOptions
            {
                VerificationUrl = "https://icyplay.example.com/verify-email",
                ExpirationHours = 24,
                SupportEmail = "support@icyplay.example.com"
            }),
            new FixedTimeProvider(now ?? RegistrationTime),
            NullLogger<EmailVerificationService>.Instance);
    }

    private static RegisterCustomerRequest CreateRequest(string email) => new(
        "Email Verification Customer",
        email,
        "StrongPassword1!",
        "+639171234567",
        true,
        "integration-test-captcha-token");

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

    private sealed class ThrowingTransactionalEmailSender : ITransactionalEmailSender
    {
        public Task SendAsync(
            TransactionalEmailMessage message,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated Mailjet failure.");
        }
    }
}
