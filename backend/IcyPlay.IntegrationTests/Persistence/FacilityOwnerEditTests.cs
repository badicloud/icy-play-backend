using System.Text.Json;
using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
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

[Collection(DatabaseCollection.Name)]
public sealed class FacilityOwnerEditTests(SqlServerDatabaseFixture database)
{
    private const string CloudName = "icyplay-test";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task UpdateBusinessAsync_ShouldSaveTheChangeAndRecordOnlyWhatMoved()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Editable Courts");

        // Act: only the phone changes.
        var result = await sut.UpdateBusinessAsync(
            owner.FacilityOwnerId,
            new UpdateBusinessRequest(
                "Abc Sports Ventures",
                "billing@example.com",
                "+639999999999",
                "DTI-123456",
                "Owner rang to correct it"),
            Admin(),
            CancellationToken.None);

        // Assert
        var entry = await LatestAsync(context, AuditAction.FacilityOwnerBusinessUpdated);
        var after = Fields(entry.NewValuesJson);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            // A diff of one line can be read; a dump of every field cannot.
            after.Should().ContainKey("billingPhone").WhoseValue.Should().Be("+639999999999");
            after.Should().HaveCount(1);
            Fields(entry.OldValuesJson)["billingPhone"].Should().Be("+639171234567");
            entry.Reason.Should().Be("Owner rang to correct it");
        }
    }

    [Fact]
    public async Task UpdateBusinessAsync_ShouldNotRecordASaveThatChangedNothing()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Unchanged Courts");
        var before = await CountAsync(context, AuditAction.FacilityOwnerBusinessUpdated);

        // Act: the same values that are already stored.
        await sut.UpdateBusinessAsync(
            owner.FacilityOwnerId,
            new UpdateBusinessRequest(
                "Abc Sports Ventures",
                "billing@example.com",
                "+639171234567",
                "DTI-123456",
                null),
            Admin(),
            CancellationToken.None);

        // Assert: an audit full of "nothing changed" buries the entries that
        // matter.
        (await CountAsync(context, AuditAction.FacilityOwnerBusinessUpdated)).Should().Be(before);
    }

    [Fact]
    public async Task UpdateFacilityAsync_ShouldKeepTheSlugWhenTheNameChanges()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Original Courts");

        // Act
        await sut.UpdateFacilityAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            FacilityRequest("Renamed Courts"),
            Admin(),
            CancellationToken.None);

        // Assert: the slug is the public web address, and a rename must not
        // quietly break links already shared.
        var facility = await context.Facilities.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == owner.FacilityId);

        using (new AssertionScope())
        {
            facility.Name.Should().Be("Renamed Courts");
            facility.Slug.Should().Be("original-courts");
        }
    }

    [Fact]
    public async Task UpdateFacilityAsync_ShouldRefuseAFacilityBelongingToAnotherOwner()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (first, sut) = await OnboardAsync(context, "First Owner Courts");
        var (second, _) = await OnboardAsync(context, "Second Owner Courts");

        // Act
        var result = await sut.UpdateFacilityAsync(
            first.FacilityOwnerId,
            second.FacilityId,
            FacilityRequest("Hijacked Courts"),
            Admin(),
            CancellationToken.None);

        // Assert: answered the same way as a missing record, so the endpoint
        // cannot be used to discover which ids exist.
        result.Failure.Should().Be(EditFailure.NotFound);
    }

    [Fact]
    public async Task UpdateFacilityAsync_ShouldReplaceTheAmenitySelection()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Amenity Edit Courts");
        var replacement = await context.Amenities
            .OrderByDescending(amenity => amenity.Key)
            .Take(2)
            .Select(amenity => amenity.Id)
            .ToArrayAsync();

        // Act
        await sut.UpdateFacilityAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            FacilityRequest("Amenity Edit Courts") with { AmenityIds = replacement },
            Admin(),
            CancellationToken.None);

        // Assert
        var attached = await context.FacilityAmenities.AsNoTracking()
            .Where(link => link.FacilityId == owner.FacilityId)
            .Select(link => link.AmenityId)
            .ToArrayAsync();

        attached.Should().BeEquivalentTo(replacement);
    }

    [Fact]
    public async Task UpdateOperatingHoursAsync_ShouldReplaceTheWeekAndRecordTheDaysThatMoved()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Hours Edit Courts");

        // Act: Sunday closes, the rest are unchanged.
        await sut.UpdateOperatingHoursAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            new UpdateOperatingHoursRequest(
                [
                    .. Enum.GetValues<DayOfWeek>().Select(day => day == DayOfWeek.Sunday
                        ? new OperatingHourInput(day, null, null)
                        : new OperatingHourInput(day, new TimeOnly(6, 0), new TimeOnly(22, 0)))
                ],
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        var hours = await context.FacilityOperatingHours.AsNoTracking()
            .Where(hour => hour.FacilityId == owner.FacilityId)
            .ToListAsync();
        var entry = await LatestAsync(context, AuditAction.FacilityHoursUpdated);

        using (new AssertionScope())
        {
            hours.Should().HaveCount(7);
            hours.Single(hour => hour.DayOfWeek == DayOfWeek.Sunday).IsClosed.Should().BeTrue();
            Fields(entry.NewValuesJson).Should().ContainKey("Sunday").WhoseValue.Should().Be("closed");
            Fields(entry.NewValuesJson).Should().HaveCount(1);
        }
    }

    [Fact]
    public async Task RenewContractAsync_ShouldRefuseATermThatOverlapsALiveOne()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Overlap Courts");

        // Act: the onboarding term already runs from today for a year.
        var result = await sut.RenewContractAsync(
            owner.FacilityOwnerId,
            new RenewContractRequest(Today.AddMonths(6), Today.AddMonths(18), null, SignedAgreement(), null),
            Admin(),
            CancellationToken.None);

        // Assert: two live terms covering one day cannot both be the one fees
        // are calculated against.
        result.Failure.Should().Be(EditFailure.OverlappingContract);
    }

    [Fact]
    public async Task RenewContractAsync_ShouldAddATermBesideTheOldOneWhenItFollowsOn()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Renewal Courts");

        // Act
        var result = await sut.RenewContractAsync(
            owner.FacilityOwnerId,
            new RenewContractRequest(Today.AddYears(1).AddDays(1), Today.AddYears(2), "Second term", SignedAgreement(), null),
            Admin(),
            CancellationToken.None);

        // Assert: last year's term and this year's coexist, which is why
        // contracts are their own table.
        var contracts = await context.FacilityOwnerContracts.AsNoTracking()
            .Where(contract => contract.FacilityOwnerId == owner.FacilityOwnerId)
            .ToListAsync();

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            contracts.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task CancelContractAsync_ShouldRefuseToCancelATermTwice()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Cancel Twice Courts");
        var contractId = await context.FacilityOwnerContracts
            .Where(contract => contract.FacilityOwnerId == owner.FacilityOwnerId)
            .Select(contract => contract.Id)
            .SingleAsync();
        await sut.CancelContractAsync(
            owner.FacilityOwnerId,
            contractId,
            new CancelContractRequest("Owner withdrew"),
            Admin(),
            CancellationToken.None);

        // Act
        var second = await sut.CancelContractAsync(
            owner.FacilityOwnerId,
            contractId,
            new CancelContractRequest(null),
            Admin(),
            CancellationToken.None);

        // Assert
        second.Failure.Should().Be(EditFailure.AlreadyCancelled);
    }

    [Fact]
    public async Task RenewContractAsync_ShouldRefuseAnAgreementThatIsNotOnOurOwnCloud()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Untrusted Agreement Courts");

        // Act
        var result = await sut.RenewContractAsync(
            owner.FacilityOwnerId,
            new RenewContractRequest(
                Today.AddYears(1).AddDays(1),
                Today.AddYears(2),
                null,
                SignedAgreement() with
                {
                    SecureUrl = "https://attacker.example/icyplay-test/image/upload/agreement.pdf"
                },
                null),
            Admin(),
            CancellationToken.None);

        // Assert: the browser posts this metadata, so it is never taken on trust.
        result.Failure.Should().Be(EditFailure.UntrustedContractDocument);
    }

    [Fact]
    public async Task ReplaceContractDocumentAsync_ShouldSwapTheAgreementAndRecordBothNames()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Rescan Courts");
        var contractId = await context.FacilityOwnerContracts
            .Where(contract => contract.FacilityOwnerId == owner.FacilityOwnerId)
            .Select(contract => contract.Id)
            .SingleAsync();

        // Act: the first scan was unreadable.
        var result = await sut.ReplaceContractDocumentAsync(
            owner.FacilityOwnerId,
            contractId,
            new ReplaceContractDocumentRequest(
                SignedAgreement() with { FileName = "agreement-rescanned.pdf" },
                "First scan was unreadable"),
            Admin(),
            CancellationToken.None);

        // Assert
        var contract = await context.FacilityOwnerContracts.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == contractId);
        var entry = await LatestAsync(context, AuditAction.ContractDocumentReplaced);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            contract.DocumentFileName.Should().Be("agreement-rescanned.pdf");
            // Both names, so the swap can be followed afterwards.
            Fields(entry.OldValuesJson)["agreement"].Should().Be("agreement.pdf");
            Fields(entry.NewValuesJson)["agreement"].Should().Be("agreement-rescanned.pdf");
            entry.Reason.Should().Be("First scan was unreadable");
        }
    }

    [Fact]
    public async Task ListActivityAsync_ShouldStartWithTheOnboardingAndGatherEveryRecord()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (owner, sut) = await OnboardAsync(context, "Activity Courts");
        await sut.UpdateBusinessAsync(
            owner.FacilityOwnerId,
            new UpdateBusinessRequest("Renamed Ventures", "billing@example.com", "+639171234567", "DTI-123456", null),
            Admin(),
            CancellationToken.None);
        await sut.UpdateFacilityAsync(
            owner.FacilityOwnerId,
            owner.FacilityId,
            FacilityRequest("Activity Courts") with { City = "Cagayan de Oro" },
            Admin(),
            CancellationToken.None);

        // Act
        var activity = await sut.ListActivityAsync(owner.FacilityOwnerId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            // Without the onboarding entry an owner appears to have come from
            // nowhere.
            activity.Should().Contain(entry => entry.Action == AuditAction.FacilityOwnerOnboarded);
            activity.Should().Contain(entry => entry.Action == AuditAction.FacilityOwnerBusinessUpdated);
            // Recorded against the facility, still gathered under its owner.
            activity.Should().Contain(entry => entry.Action == AuditAction.FacilityUpdated);
            activity.Select(entry => entry.CreatedAt).Should().BeInDescendingOrder();
        }
    }

    private static UpdateFacilityRequest FacilityRequest(string name) => new(
        name,
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
        null);

    private static async Task<(OnboardedFacilityOwnerResponse Owner, FacilityOwnerEditService Service)>
        OnboardAsync(AppDbContext context, string facilityName)
    {
        var onboarding = new FacilityOwnerOnboardingService(
            context,
            new PasswordHasher<User>(),
            new CloudinaryAssetService(
                Options.Create(new CloudinaryOptions
                {
                    CloudName = CloudName,
                    ApiKey = "123456789012345",
                    ApiSecret = "test-api-secret"
                }),
                new FixedTimeProvider(Now)),
            new SilentInvitation(),
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now),
            NullLogger<FacilityOwnerOnboardingService>.Instance);

        var result = await onboarding.OnboardAsync(
            Request(facilityName),
            Admin(),
            CancellationToken.None);

        var edits = new FacilityOwnerEditService(
            context,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            Assets(),
            new FixedTimeProvider(Now),
            NullLogger<FacilityOwnerEditService>.Instance);

        return (result.Value!, edits);
    }

    private static async Task<AuditLog> LatestAsync(AppDbContext context, string action) =>
        await context.AuditLogs
            .AsNoTracking()
            .Where(entry => entry.Action == action)
            .OrderByDescending(entry => entry.Id)
            .FirstAsync();

    private static async Task<int> CountAsync(AppDbContext context, string action) =>
        await context.AuditLogs.CountAsync(entry => entry.Action == action);

    private static Dictionary<string, string?> Fields(string? json) =>
        json is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string?>>(json)!;

    private static AuditActor Admin() =>
        new(Guid.NewGuid(), UserRoleName.PlatformAdmin, "127.0.0.1", "tests");

    private static OnboardFacilityOwnerRequest Request(string facilityName) => new(
        new OwnerAccountInput("Juan Dela Cruz", $"edit-{Guid.NewGuid():N}@example.com", "+639171234567"),
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
            []),
        [.. Enum.GetValues<DayOfWeek>().Select(day => new OperatingHourInput(
            day,
            new TimeOnly(6, 0),
            new TimeOnly(22, 0)))],
        new ContractInput(Today, Today.AddYears(1), "Signed on encoding.", SignedAgreement()));

    private static CloudinaryAssetService Assets() => new(
        Options.Create(new CloudinaryOptions
        {
            CloudName = CloudName,
            ApiKey = "123456789012345",
            ApiSecret = "test-api-secret"
        }),
        new FixedTimeProvider(Now));

    /// <summary>A signed agreement on our own cloud, which every term needs.</summary>
    private static UploadedFileInput SignedAgreement() => new(
        "icyplay/facility-owners/contracts/agreement",
        $"https://res.cloudinary.com/{CloudName}/image/upload/v1/agreement.pdf",
        "agreement.pdf",
        "application/pdf",
        4096);

    private sealed class SilentInvitation : IAccountInvitationService
    {
        public Task SendAsync(
            Guid userId,
            string recipientEmail,
            string recipientName,
            string businessName,
            CancellationToken ct) => Task.CompletedTask;

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
