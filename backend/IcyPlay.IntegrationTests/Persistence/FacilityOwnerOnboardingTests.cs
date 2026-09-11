using FluentAssertions.Execution;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class FacilityOwnerOnboardingTests(SqlServerDatabaseFixture database)
{
    private const string CloudName = "icyplay-test";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task OnboardAsync_ShouldCreateTheWholeGraphAndCommenceTheOwner()
    {
        // Arrange
        await using var context = database.CreateContext();
        var email = UniqueEmail();
        var (sut, invitations) = CreateService(context);

        // Act
        var result = await sut.OnboardAsync(
            CreateRequest(email, "Abc Sports Center"),
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();

        var owner = await context.FacilityOwners
            .Include(candidate => candidate.Documents)
            .Include(candidate => candidate.Contracts)
            .Include(candidate => candidate.Facilities)
            .ThenInclude(facility => facility.OperatingHours)
            .SingleAsync(candidate => candidate.Id == result.Value!.FacilityOwnerId);

        var roles = await context.UserRoles
            .Where(role => role.UserId == result.Value!.UserId)
            .Select(role => role.Role)
            .ToListAsync();

        using (new AssertionScope())
        {
            roles.Should().Equal(UserRoleName.FacilityOwner);
            owner.BusinessRegistrationNumber.Should().Be("DTI-123456");
            owner.Documents.Should().ContainSingle();
            owner.Contracts.Should().ContainSingle();
            owner.Facilities.Should().ContainSingle();
            owner.Facilities.Single().OperatingHours.Should().HaveCount(7);
            owner.Facilities.Single().Slug.Should().Be("abc-sports-center");
            // The contract covers today, so the owner is live rather than merely
            // encoded.
            result.Value!.Status.Should().Be(FacilityOwnerStatus.Commenced.ToString());
            result.Value.InvitationEmailSent.Should().BeTrue();
            invitations.Recipients.Should().Equal(email);
        }
    }

    [Fact]
    public async Task OnboardAsync_ShouldLeaveTheOwnerPendingWhenTheContractStartsLater()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var request = CreateRequest(UniqueEmail(), "Future Courts") with
        {
            Contract = new ContractInput(Today.AddMonths(1), Today.AddMonths(13), null)
        };

        // Act
        var result = await sut.OnboardAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Assert: encoding an owner does not make them bookable.
        result.Value!.Status.Should().Be(FacilityOwnerStatus.Pending.ToString());
    }

    [Fact]
    public async Task OnboardAsync_ShouldRejectADocumentUrlThatIsNotOnOurOwnCloud()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var email = UniqueEmail();
        var request = CreateRequest(email, "Untrusted Courts") with
        {
            Documents =
            [
                CreateDocument("https://attacker.example/icyplay-test/image/upload/permit.pdf")
            ]
        };

        // Act
        var result = await sut.OnboardAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Failure.Should().Be(OnboardingFailure.UntrustedAssetUrl);
            // Rejected before anything was written, not cleaned up afterwards.
            (await context.Users.AnyAsync(user => user.Email == email)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task OnboardAsync_ShouldRejectAnEmailThatIsAlreadyInUse()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var email = UniqueEmail();
        await sut.OnboardAsync(CreateRequest(email, "First Courts"), Guid.NewGuid(), CancellationToken.None);

        // Act
        var result = await sut.OnboardAsync(
            CreateRequest(email, "Second Courts"),
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Failure.Should().Be(OnboardingFailure.DuplicateEmail);
            (await context.FacilityOwners.CountAsync(owner => owner.User.Email == email)).Should().Be(1);
        }
    }

    [Fact]
    public async Task OnboardAsync_ShouldSuffixTheSlugWhenTheNameIsAlreadyTaken()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var name = $"Twin Courts {Guid.NewGuid():N}";
        var first = await sut.OnboardAsync(
            CreateRequest(UniqueEmail(), name),
            Guid.NewGuid(),
            CancellationToken.None);

        // Act: two venues legitimately share a name across two cities.
        var second = await sut.OnboardAsync(
            CreateRequest(UniqueEmail(), name),
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            second.Succeeded.Should().BeTrue();
            second.Value!.FacilitySlug.Should().Be($"{first.Value!.FacilitySlug}-2");
        }
    }

    [Fact]
    public async Task OnboardAsync_ShouldRejectATimeZoneTheServerDoesNotKnow()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var request = CreateRequest(UniqueEmail(), "Nowhere Courts");
        request = request with { Facility = request.Facility with { TimeZone = "Mars/Olympus_Mons" } };

        // Act
        var result = await sut.OnboardAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Failure.Should().Be(OnboardingFailure.UnknownTimeZone);
    }

    [Fact]
    public async Task OnboardAsync_ShouldRejectAnAmenityThatDoesNotExist()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var request = CreateRequest(UniqueEmail(), "Phantom Amenity Courts");
        request = request with { Facility = request.Facility with { AmenityIds = [Guid.NewGuid()] } };

        // Act
        var result = await sut.OnboardAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Failure.Should().Be(OnboardingFailure.UnknownAmenity);
    }

    [Fact]
    public async Task OnboardAsync_ShouldAttachTheSelectedAmenitiesToTheFacility()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var amenityIds = await context.Amenities
            .OrderBy(amenity => amenity.Key)
            .Take(3)
            .Select(amenity => amenity.Id)
            .ToArrayAsync();
        var request = CreateRequest(UniqueEmail(), "Amenity Rich Courts");
        request = request with { Facility = request.Facility with { AmenityIds = amenityIds } };

        // Act
        var result = await sut.OnboardAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Assert
        var attached = await context.FacilityAmenities
            .Where(link => link.FacilityId == result.Value!.FacilityId)
            .Select(link => link.AmenityId)
            .ToArrayAsync();

        attached.Should().BeEquivalentTo(amenityIds);
    }

    [Fact]
    public async Task OnboardAsync_ShouldStillCompleteWhenTheInvitationEmailFails()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context, new FailingInvitation());

        // Act
        var result = await sut.OnboardAsync(
            CreateRequest(UniqueEmail(), "Mailjet Is Down Courts"),
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert: a mail outage must not undo work the admin has finished.
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            result.Value!.InvitationEmailSent.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ListAsync_ShouldRejectASortFieldThatIsNotOnTheWhitelist()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);

        // Act
        var result = await sut.ListAsync(new FacilityOwnerQuery(SortBy: "billingEmail"), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ShouldReportTheDerivedStatusAndTheFacilityCount()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);
        var businessName = $"Listable Courts {Guid.NewGuid():N}";
        var request = CreateRequest(UniqueEmail(), "Listable Courts");
        request = request with { Business = request.Business with { BusinessName = businessName } };
        await sut.OnboardAsync(request, Guid.NewGuid(), CancellationToken.None);

        // Act
        var result = await sut.ListAsync(new FacilityOwnerQuery(Search: businessName), CancellationToken.None);

        // Assert
        var row = result!.Items.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            row.Status.Should().Be(FacilityOwnerStatus.Commenced.ToString());
            row.FacilityCount.Should().Be(1);
            row.ContractStartDate.Should().Be(Today);
        }
    }

    [Fact]
    public async Task ListAmenitiesAsync_ShouldReturnTheSeededLookupGroupedByCategory()
    {
        // Arrange
        await using var context = database.CreateContext();
        var (sut, _) = CreateService(context);

        // Act
        var amenities = await sut.ListAmenitiesAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            amenities.Should().NotBeEmpty();
            amenities.Select(amenity => amenity.Key).Should().OnlyHaveUniqueItems();
            amenities.Should().Contain(amenity => amenity.Key == "parking");
        }
    }

    private static (FacilityOwnerOnboardingService Service, RecordingInvitation Invitations) CreateService(
        AppDbContext context,
        IAccountInvitationService? invitationService = null)
    {
        var recorder = invitationService as RecordingInvitation ?? new RecordingInvitation();
        var service = new FacilityOwnerOnboardingService(
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
            invitationService ?? recorder,
            new FixedTimeProvider(Now),
            NullLogger<FacilityOwnerOnboardingService>.Instance);

        return (service, recorder);
    }

    private static string UniqueEmail() => $"onboarding-{Guid.NewGuid():N}@example.com";

    private static OnboardFacilityOwnerRequest CreateRequest(string email, string facilityName) => new(
        new OwnerAccountInput("Juan Dela Cruz", email, "+639171234567"),
        new BusinessInput("Abc Sports Ventures", "billing@example.com", "+639171234567", "DTI-123456"),
        [CreateDocument($"https://res.cloudinary.com/{CloudName}/image/upload/v1/permit.pdf")],
        new FacilityInput(
            facilityName,
            "Six covered courts.",
            "123 Quimpo Boulevard",
            null,
            "Davao City",
            "Davao del Sur",
            "8000",
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
        new ContractInput(Today, Today.AddYears(1), "Signed at the Davao office."));

    private static OwnerDocumentInput CreateDocument(string secureUrl) => new(
        FacilityOwnerDocumentType.BusinessPermit,
        "icyplay/facility-owners/documents/permit",
        secureUrl,
        "permit.pdf",
        "application/pdf",
        2048);

    private sealed class RecordingInvitation : IAccountInvitationService
    {
        public List<string> Recipients { get; } = [];

        public Task SendAsync(
            Guid userId,
            string recipientEmail,
            string recipientName,
            string businessName,
            CancellationToken ct)
        {
            Recipients.Add(recipientEmail);
            return Task.CompletedTask;
        }

        public Task<InvitationDetails?> CheckAsync(string rawToken, CancellationToken ct) =>
            Task.FromResult<InvitationDetails?>(null);

        public Task<InvitationAcceptance> AcceptAsync(string rawToken, string password, CancellationToken ct) =>
            Task.FromResult(InvitationAcceptance.InvalidToken);
    }

    private sealed class FailingInvitation : IAccountInvitationService
    {
        public Task SendAsync(
            Guid userId,
            string recipientEmail,
            string recipientName,
            string businessName,
            CancellationToken ct) =>
            throw new InvalidOperationException("Mailjet is unavailable.");

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
