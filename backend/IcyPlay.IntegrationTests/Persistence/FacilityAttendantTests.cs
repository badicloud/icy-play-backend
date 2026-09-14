using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

/// <summary>
/// Who works a venue's desk, and who is turned away from it.
///
/// The turning away matters as much as the adding: the same address twice is
/// the mistake an admin actually makes, and the unique index behind this table
/// answers it with a database error rather than a sentence anybody can read.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class FacilityAttendantTests(SqlServerDatabaseFixture database)
{
    private const string CloudName = "icyplay-test";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task InviteAsync_ShouldRefuseAnAddressThatIsAlreadyOnThisDesk()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, _, sut) = await OnboardAsync(context, "Twice Courts");
        var email = $"desk-{Guid.NewGuid():N}@example.com";

        await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Ana Reyes", email, null, null),
            Admin(),
            CancellationToken.None);

        // Act: the same address again, typed the way somebody would type it.
        var again = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Ana Reyes", email.ToUpperInvariant(), null, null),
            Admin(),
            CancellationToken.None);

        // Assert
        var rows = await context.FacilityAttendants
            .AsNoTracking()
            .CountAsync(attendant => attendant.FacilityId == owner.FacilityId);

        using (new AssertionScope())
        {
            again.Succeeded.Should().BeFalse();
            again.Failure.Should().Be(AttendantFailure.AlreadyAttending);
            // Refused, not half-done: no second row, and no second account.
            rows.Should().Be(1);
            (await context.Users.CountAsync(user => user.Email == email.ToLowerInvariant()))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task InviteAsync_ShouldRefuseTheOwnersOwnAddress()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, ownerEmail, sut) = await OnboardAsync(context, "Owner Courts");

        // Act
        var result = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Juan Dela Cruz", ownerEmail, null, null),
            Admin(),
            CancellationToken.None);

        // Assert: they attend it by owning it. A row would be a second answer
        // to a question the ownership already settles.
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AttendantFailure.IsTheOwner);
            (await context.FacilityAttendants.CountAsync(x => x.FacilityId == owner.FacilityId))
                .Should().Be(0);
        }
    }

    [Fact]
    public async Task InviteAsync_ShouldRefuseAnAddressSomebodyElseAlreadyHoldsAnAccountAt()
    {
        // Arrange: a customer who signed up months ago and has never been near
        // this venue.
        await using var context = database.CreateContext();
        var (owner, _, sut) = await OnboardAsync(context, "Customer Courts");
        var customer = await CustomerAsync(context, "Rosa Cruz");

        // Act
        var result = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Rosa Cruz", customer.Email, null, null),
            Admin(),
            CancellationToken.None);

        // Assert: an admin who mistypes a colleague's address must not hand a
        // stranger a venue's bookings, so a taken address is refused outright.
        var roles = await context.UserRoles
            .AsNoTracking()
            .CountAsync(role => role.UserId == customer.Id);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AttendantFailure.EmailAlreadyRegistered);
            (await context.FacilityAttendants.CountAsync(x => x.FacilityId == owner.FacilityId))
                .Should().Be(0);
            // Refused means nothing was granted either.
            roles.Should().Be(1);
        }
    }

    [Fact]
    public async Task CheckEmailAsync_ShouldTellTheConsoleWhatItMayDoWithAnAddress()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, ownerEmail, sut) = await OnboardAsync(context, "Checking Courts");
        var customer = await CustomerAsync(context, "Rosa Cruz");
        var working = $"working-{Guid.NewGuid():N}@example.com";
        var left = $"left-{Guid.NewGuid():N}@example.com";

        await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Still Here", working, null, null),
            Admin(),
            CancellationToken.None);

        var leaving = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Went Away", left, null, null),
            Admin(),
            CancellationToken.None);

        await sut.RemoveAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            leaving.Value!.Id!.Value,
            null,
            Admin(),
            CancellationToken.None);

        // Act
        var fresh = await CheckAsync(sut, owner, $"nobody-{Guid.NewGuid():N}@example.com");
        var theOwner = await CheckAsync(sut, owner, ownerEmail.ToUpperInvariant());
        var onDesk = await CheckAsync(sut, owner, working);
        var wasHere = await CheckAsync(sut, owner, left);
        var taken = await CheckAsync(sut, owner, customer.Email);

        // Assert
        using (new AssertionScope())
        {
            fresh.Status.Should().Be(AttendantEmailStatus.Available);
            fresh.CanBeAdded.Should().BeTrue();
            // Typed in capitals, answered all the same.
            theOwner.Status.Should().Be(AttendantEmailStatus.IsTheOwner);
            theOwner.CanBeAdded.Should().BeFalse();
            onDesk.Status.Should().Be(AttendantEmailStatus.AlreadyAttending);
            onDesk.CanBeAdded.Should().BeFalse();
            // Somebody who worked here before holds an account like anybody
            // else, and the console must not offer them as though they were new.
            wasHere.Status.Should().Be(AttendantEmailStatus.AlreadyRegistered);
            wasHere.CanBeAdded.Should().BeFalse();
            taken.Status.Should().Be(AttendantEmailStatus.AlreadyRegistered);
            taken.CanBeAdded.Should().BeFalse();
            // Named, so the console can say who it means rather than only that
            // somebody is in the way.
            taken.FullName.Should().Be("Rosa Cruz");
        }
    }

    [Fact]
    public async Task RemoveAsync_ShouldRetireTheRowRatherThanDeleteIt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, _, sut) = await OnboardAsync(context, "Leaving Courts");
        var added = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Ben Lim", $"ben-{Guid.NewGuid():N}@example.com", null, null),
            Admin(),
            CancellationToken.None);

        // Act
        var removed = await sut.RemoveAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            added.Value!.Id!.Value,
            "Moved to another branch",
            Admin(),
            CancellationToken.None);

        // Assert: gone from the desk, still on the record, because a booking
        // they confirmed must keep somebody's name against it.
        var row = await context.FacilityAttendants
            .AsNoTracking()
            .SingleAsync(attendant => attendant.Id == added.Value!.Id!.Value);

        var roster = await sut.ListAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            CancellationToken.None);

        using (new AssertionScope())
        {
            removed.Succeeded.Should().BeTrue();
            row.IsActive.Should().BeFalse();
            row.UpdatedAt.Should().Be(Now);
            // Only the owner is left standing at the desk.
            roster.Value!.Should().ContainSingle().Which.IsOwner.Should().BeTrue();
        }
    }

    [Fact]
    public async Task InviteAsync_ShouldRefuseSomebodyWhoWasTakenOffThisDeskBefore()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, _, sut) = await OnboardAsync(context, "Returning Courts");
        var email = $"back-{Guid.NewGuid():N}@example.com";
        var added = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Cel Ramos", email, null, null),
            Admin(),
            CancellationToken.None);

        await sut.RemoveAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            added.Value!.Id!.Value,
            null,
            Admin(),
            CancellationToken.None);

        // Act
        var back = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Cel Ramos", email, null, null),
            Admin(),
            CancellationToken.None);

        // Assert: the rule has no exception. Their address has an account now,
        // like anybody else's, and the retired row stays retired.
        using (new AssertionScope())
        {
            back.Succeeded.Should().BeFalse();
            back.Failure.Should().Be(AttendantFailure.EmailAlreadyRegistered);
            (await context.FacilityAttendants.AsNoTracking()
                .SingleAsync(x => x.Id == added.Value!.Id!.Value)).IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RemoveAsync_ShouldRefuseADeskAtAnotherOwnersVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (mine, _, sut) = await OnboardAsync(context, "Mine Courts");
        var (theirs, _, _) = await OnboardAsync(context, "Theirs Courts");
        var added = await sut.InviteAsync(
            theirs.FacilityOwnerId,
            theirs.FacilityId,
            new InviteAttendantRequest("Not Mine", $"not-{Guid.NewGuid():N}@example.com", null, null),
            Admin(),
            CancellationToken.None);

        // Act: my owner id, their attendant.
        var result = await sut.RemoveAsync(
            mine.FacilityOwnerId,
            theirs.FacilityId,
            added.Value!.Id!.Value,
            null,
            Admin(),
            CancellationToken.None);

        // Assert: another owner's venue answers the same as one that is not
        // there at all, so the console cannot be used to map what it cannot see.
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AttendantFailure.FacilityNotFound);
            (await context.FacilityAttendants.AsNoTracking()
                .SingleAsync(x => x.Id == added.Value!.Id!.Value)).IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ResendInvitationAsync_ShouldSendAnotherLetterAndCountIt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mail = new RecordingInvitation(context);
        var (owner, _, sut) = await OnboardAsync(context, "Resending Courts", mail);
        var added = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Waiting Wilma", $"wilma-{Guid.NewGuid():N}@example.com", null, null),
            Admin(),
            CancellationToken.None);

        // Act: twice, because an admin who hears nothing tries again.
        await sut.ResendInvitationAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            added.Value!.Id!.Value,
            Admin(),
            CancellationToken.None);

        var second = await sut.ResendInvitationAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            added.Value!.Id!.Value,
            Admin(),
            CancellationToken.None);

        // Assert
        var roster = await sut.ListAsync(owner.FacilityOwnerId, owner.FacilityId, CancellationToken.None);
        var row = roster.Value!.Single(attendant => attendant.Id == added.Value!.Id);

        var trail = await context.AuditLogs
            .AsNoTracking()
            .CountAsync(entry => entry.Action == AuditAction.FacilityAttendantInvitationSent
                && entry.EntityId == owner.FacilityId);

        using (new AssertionScope())
        {
            second.Succeeded.Should().BeTrue();
            // The first invitation and both resends.
            row.InvitationsSent.Should().Be(3);
            row.LastInvitedAt.Should().Be(Now);
            row.HasAccepted.Should().BeFalse();
            // Each press is its own line in the trail, so how often somebody
            // was chased is answerable later.
            trail.Should().Be(2);
            // The attendant's letter, naming the venue they are being put on.
            mail.Sent.Should().AllSatisfy(request =>
            {
                request.TemplateKey.Should().Be(EmailTemplateKey.FacilityAttendantInvitation);
                request.FacilityName.Should().Be("Resending Courts");
            });
        }
    }

    [Fact]
    public async Task ResendInvitationAsync_ShouldRefuseSomebodyWhoHasAlreadyClaimedTheirAccount()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mail = new RecordingInvitation(context);
        var (owner, _, sut) = await OnboardAsync(context, "Settled Courts", mail);
        var added = await sut.InviteAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new InviteAttendantRequest("Started Sam", $"sam-{Guid.NewGuid():N}@example.com", null, null),
            Admin(),
            CancellationToken.None);

        var token = await context.AccountInvitationTokens
            .SingleAsync(candidate => candidate.UserId == added.Value!.UserId);
        token.Accept(Now);
        await context.SaveChangesAsync();

        var before = mail.Sent.Count;

        // Act
        var result = await sut.ResendInvitationAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            added.Value!.Id!.Value,
            Admin(),
            CancellationToken.None);

        // Assert: a fresh link would be an offer to replace a password they are
        // already using, which is not what "resend" means.
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(AttendantFailure.InvitationAlreadyAccepted);
            mail.Sent.Should().HaveCount(before);
        }
    }

    private static async Task<AttendantEmailCheck> CheckAsync(
        FacilityAttendantService sut,
        OnboardedFacilityOwnerResponse owner,
        string email) =>
        (await sut.CheckEmailAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            email,
            CancellationToken.None)).Value!;

    /// <summary>Somebody who signed up to book a court, months before any of this.</summary>
    private static async Task<User> CustomerAsync(AppDbContext context, string name)
    {
        var customer = new User($"customer-{Guid.NewGuid():N}@example.com", name, null);
        customer.SetPasswordHash("hashed");
        context.Users.Add(customer);
        context.UserRoles.Add(new UserRole(customer.Id, UserRoleName.Customer));
        await context.SaveChangesAsync();

        return customer;
    }

    private static async Task<(OnboardedFacilityOwnerResponse Owner, string OwnerEmail, FacilityAttendantService Service)>
        OnboardAsync(AppDbContext context, string facilityName, RecordingInvitation? mail = null)
    {
        var time = new FixedTimeProvider(Now);
        var onboarding = new FacilityOwnerOnboardingService(
            context,
            new PasswordHasher<User>(),
            Assets(),
            new RecordingInvitation(context),
            new AuditLogger(context, time),
            time,
            NullLogger<FacilityOwnerOnboardingService>.Instance);

        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";
        var result = await onboarding.OnboardAsync(
            Request(facilityName, ownerEmail),
            Admin(),
            CancellationToken.None);

        var attendants = new FacilityAttendantService(
            context,
            mail ?? new RecordingInvitation(context),
            new PasswordHasher<User>(),
            new AuditLogger(context, time),
            time,
            NullLogger<FacilityAttendantService>.Instance);

        return (result.Value!, ownerEmail, attendants);
    }

    private static AuditActor Admin() =>
        new(Guid.NewGuid(), UserRoleName.PlatformAdmin, "127.0.0.1", "tests");

    private static CloudinaryAssetService Assets() => new(
        Options.Create(new CloudinaryOptions
        {
            CloudName = CloudName,
            ApiKey = "123456789012345",
            ApiSecret = "test-api-secret"
        }),
        new FixedTimeProvider(Now));

    private static OnboardFacilityOwnerRequest Request(string facilityName, string ownerEmail) => new(
        new OwnerAccountInput("Juan Dela Cruz", ownerEmail, "+639171234567"),
        new BusinessInput("Abc Sports Ventures", "billing@example.com", "+639171234567", "DTI-123456"),
        [
            new OwnerDocumentInput(
                FacilityOwnerDocumentType.BusinessPermit,
                "icyplay/facility-owners/documents/permit",
                $"https://res.cloudinary.com/{CloudName}/image/upload/v1/permit.pdf",
                "permit.pdf",
                "application/pdf",
                2048)
        ],
        new FacilityInput(
            facilityName,
            "Six covered courts.",
            "123 Main Street",
            null,
            "Cebu City",
            "Cebu",
            "6000",
            "Philippines",
            null,
            null,
            "Asia/Manila",
            "+639171234567",
            "hello@example.com",
            "First aid kit on site.",
            "No street shoes on the court.",
            [],
            []),
        [.. Enum.GetValues<DayOfWeek>().Select(day => new OperatingHourInput(
            day,
            new TimeOnly(6, 0),
            new TimeOnly(22, 0)))],
        new ContractInput(Today, Today.AddYears(1), "Signed on encoding.", SignedAgreement()));

    private static UploadedFileInput SignedAgreement() => new(
        "icyplay/facility-owners/contracts/agreement",
        $"https://res.cloudinary.com/{CloudName}/image/upload/v1/agreement.pdf",
        "agreement.pdf",
        "application/pdf",
        4096);

    /// <summary>
    /// Stands in for the invitation service, writing the token row a real send
    /// writes. The roster counts those rows, so a double that skipped them
    /// would let a test agree with itself about a number nobody stored.
    /// </summary>
    private sealed class RecordingInvitation(AppDbContext context) : IAccountInvitationService
    {
        public List<InvitationRequest> Sent { get; } = [];

        public async Task SendAsync(InvitationRequest request, CancellationToken ct)
        {
            Sent.Add(request);

            // The same retirement the real one performs: only the newest link
            // may be claimed.
            await context.AccountInvitationTokens
                .Where(token => token.UserId == request.UserId && token.AcceptedAt == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(token => token.ExpiresAt, Now),
                    ct);

            context.AccountInvitationTokens.Add(new AccountInvitationToken(
                request.UserId,
                $"hash-{Guid.NewGuid():N}",
                Now.AddDays(7),
                Now));
            await context.SaveChangesAsync(ct);
        }

        public Task<InvitationDetails?> CheckAsync(string rawToken, CancellationToken ct) =>
            Task.FromResult<InvitationDetails?>(null);

        public Task<InvitationAcceptance> AcceptAsync(string rawToken, string password, CancellationToken ct) =>
            Task.FromResult(InvitationAcceptance.InvalidToken);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
