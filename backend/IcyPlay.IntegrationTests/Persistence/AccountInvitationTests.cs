using System.Security.Cryptography;
using System.Text;
using FluentAssertions.Execution;
using IcyPlay.Application.Email;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class AccountInvitationTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SendAsync_ShouldEmailALinkAndStoreOnlyItsHash()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context);
        var (sut, mail) = CreateService(context);

        // Act
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc Sports Ventures", CancellationToken.None);

        // Assert
        var stored = await context.AccountInvitationTokens
            .SingleAsync(token => token.UserId == user.Id);
        var rawToken = TokenFrom(mail.LastVariables["activation_url"].ToString()!);

        using (new AssertionScope())
        {
            mail.LastTemplateKey.Should().Be("facility-owner-invitation");
            mail.LastVariables["business_name"].Should().Be("Abc Sports Ventures");
            // Seven days, not the hour a password reset gets.
            stored.ExpiresAt.Should().Be(Now.AddDays(7));
            // The raw value lives in the link and nowhere else, so a leaked
            // database cannot be used to claim the account.
            stored.TokenHash.Should().Be(Hash(rawToken));
            stored.TokenHash.Should().NotBe(rawToken);
        }
    }

    [Fact]
    public async Task SendAsync_ShouldRetireAnInvitationThatIsStillOutstanding()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context);
        var (sut, mail) = CreateService(context);
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc", CancellationToken.None);
        var firstToken = TokenFrom(mail.LastVariables["activation_url"].ToString()!);

        // Act
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc", CancellationToken.None);
        var secondToken = TokenFrom(mail.LastVariables["activation_url"].ToString()!);

        // Assert: two live invitations to one account is one more than anybody
        // needs, so the older link stops working.
        using (new AssertionScope())
        {
            (await sut.CheckAsync(firstToken, CancellationToken.None)).Should().BeNull();
            (await sut.CheckAsync(secondToken, CancellationToken.None)).Should().NotBeNull();
        }
    }

    [Fact]
    public async Task CheckAsync_ShouldReturnTheDetailsTheActivationPagePrefills()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context, "Juan Dela Cruz", "+639953979930");
        context.FacilityOwners.Add(new FacilityOwner(user.Id, "Abc Sports Ventures", "billing@example.com", null));
        await context.SaveChangesAsync();
        var (sut, mail) = CreateService(context);
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc Sports Ventures", CancellationToken.None);

        // Act
        var details = await sut.CheckAsync(
            TokenFrom(mail.LastVariables["activation_url"].ToString()!),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            details!.FullName.Should().Be("Juan Dela Cruz");
            details.Email.Should().Be(user.Email);
            details.PhoneNumber.Should().Be("+639953979930");
            // Which of their venues this account is for.
            details.BusinessName.Should().Be("Abc Sports Ventures");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a-token-nobody-issued")]
    public async Task CheckAsync_ShouldReturnNullForATokenThatWasNeverIssued(string rawToken)
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);

        // Act
        var details = await sut.CheckAsync(rawToken, CancellationToken.None);

        // Assert
        details.Should().BeNull();
    }

    [Fact]
    public async Task AcceptAsync_ShouldSetThePasswordAndVerifyTheEmailInOneStep()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context);
        var originalHash = user.PasswordHash;
        var (sut, mail) = CreateService(context);
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc", CancellationToken.None);
        var rawToken = TokenFrom(mail.LastVariables["activation_url"].ToString()!);

        // Act
        var result = await sut.AcceptAsync(rawToken, "Str0ng!Passw0rd", CancellationToken.None);

        // Assert
        var updated = await context.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == user.Id);
        using (new AssertionScope())
        {
            result.Should().Be(InvitationAcceptance.Accepted);
            updated.PasswordHash.Should().NotBe(originalHash);
            // Opening a link sent to an address proves control of it, so a
            // second verification email would establish nothing.
            updated.EmailVerifiedAt.Should().Be(Now);
        }
    }

    [Fact]
    public async Task AcceptAsync_ShouldTellASpentInvitationApartFromABadOne()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context);
        var (sut, mail) = CreateService(context);
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc", CancellationToken.None);
        var rawToken = TokenFrom(mail.LastVariables["activation_url"].ToString()!);
        await sut.AcceptAsync(rawToken, "Str0ng!Passw0rd", CancellationToken.None);

        // Act
        var second = await sut.AcceptAsync(rawToken, "An0ther!Passw0rd", CancellationToken.None);

        // Assert: "already used" sends the owner to sign in, where "invalid"
        // sends them hunting for a typo that is not there.
        second.Should().Be(InvitationAcceptance.AlreadyAccepted);
    }

    [Fact]
    public async Task AcceptAsync_ShouldRefuseAnInvitationThatHasRunOut()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = await AddUserAsync(context);
        var (sut, mail) = CreateService(context);
        await sut.SendAsync(user.Id, user.Email, user.FullName, "Abc", CancellationToken.None);
        var rawToken = TokenFrom(mail.LastVariables["activation_url"].ToString()!);

        // A clock eight days on, one day past the window.
        var expired = CreateService(context, Now.AddDays(8)).Service;

        // Act
        var result = await expired.AcceptAsync(rawToken, "Str0ng!Passw0rd", CancellationToken.None);

        // Assert
        result.Should().Be(InvitationAcceptance.ExpiredToken);
    }

    [Fact]
    public async Task AcceptAsync_ShouldRefuseATokenThatWasNeverIssued()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);

        // Act
        var result = await sut.AcceptAsync("not-a-real-token", "Str0ng!Passw0rd", CancellationToken.None);

        // Assert
        result.Should().Be(InvitationAcceptance.InvalidToken);
    }

    private static (AccountInvitationService Service, RecordingEmailSender Mail) CreateService(
        AppDbContext context,
        DateTimeOffset? now = null)
    {
        var mail = new RecordingEmailSender();
        var service = new AccountInvitationService(
            context,
            mail,
            new PasswordHasher<User>(),
            Options.Create(new AccountInvitationOptions
            {
                ActivationUrl = "https://app.example.com/accept-invitation",
                ExpirationDays = 7,
                SupportEmail = "support@example.com"
            }),
            new FixedTimeProvider(now ?? Now),
            NullLogger<AccountInvitationService>.Instance);

        return (service, mail);
    }

    private static async Task<User> AddUserAsync(
        AppDbContext context,
        string fullName = "Invited Owner",
        string? phoneNumber = null)
    {
        var user = new User($"invite-{Guid.NewGuid():N}@example.com", fullName, phoneNumber);
        user.SetPasswordHash("the-random-one-nobody-knows");
        context.Users.Add(user);
        context.UserRoles.Add(new UserRole(user.Id, UserRoleName.FacilityOwner));
        await context.SaveChangesAsync();
        return user;
    }

    private static string TokenFrom(string activationUrl) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(activationUrl).Query)["token"]!;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class RecordingEmailSender : ITransactionalEmailSender
    {
        public string? LastTemplateKey
        {
            get; private set;
        }
        public IReadOnlyDictionary<string, object> LastVariables { get; private set; } =
            new Dictionary<string, object>();

        public Task SendAsync(TransactionalEmailMessage message, CancellationToken ct)
        {
            LastTemplateKey = message.TemplateKey;
            LastVariables = message.Variables;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
