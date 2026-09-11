using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
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
public sealed class FacilityOwnerDetailTests(SqlServerDatabaseFixture database)
{
    private const string CloudName = "icyplay-test";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    [Fact]
    public async Task GetAsync_ShouldReturnNullWhenNoOwnerHasThatId()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act
        var detail = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert: null is what lets the API answer 404 instead of an empty shell.
        detail.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ShouldReturnTheWholeGraphInOneRead()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var amenityIds = await context.Amenities
            .OrderBy(amenity => amenity.Key)
            .Take(2)
            .Select(amenity => amenity.Id)
            .ToArrayAsync();
        var adminUserId = await AddAdminAsync(context, "Detail Admin");
        var onboarded = await sut.OnboardAsync(
            CreateRequest(UniqueEmail(), "Detail Courts", amenityIds),
            Admin(adminUserId),
            CancellationToken.None);

        // Act
        var detail = await sut.GetAsync(onboarded.Value!.FacilityOwnerId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            detail.Should().NotBeNull();
            detail!.BusinessName.Should().Be("Abc Sports Ventures");
            detail.BusinessRegistrationNumber.Should().Be("DTI-123456");
            detail.Status.Should().Be(FacilityOwnerStatus.Commenced.ToString());

            detail.Owner.FullName.Should().Be("Juan Dela Cruz");
            // Encoded by an admin, so the owner has not confirmed the address yet.
            detail.Owner.IsEmailVerified.Should().BeFalse();

            detail.Documents.Should().ContainSingle();
            detail.Documents.Single().SecureUrl.Should().StartWith("https://");

            var facility = detail.Facilities.Should().ContainSingle().Subject;
            facility.Slug.Should().Be("detail-courts");
            facility.OperatingHours.Should().HaveCount(7);
            facility.Amenities.Select(amenity => amenity.Id).Should().BeEquivalentTo(amenityIds);

            var contract = detail.Contracts.Should().ContainSingle().Subject;
            contract.IsLiveToday.Should().BeTrue();
            // Traceable to a person, not just to an id.
            contract.CommencedByName.Should().Be("Detail Admin");
        }
    }

    [Fact]
    public async Task GetAsync_ShouldNotMarkAFutureContractAsLiveToday()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var request = CreateRequest(UniqueEmail(), "Future Detail Courts", []) with
        {
            Contract = new ContractInput(Today.AddMonths(1), Today.AddMonths(13), null, SignedAgreement())
        };
        var onboarded = await sut.OnboardAsync(request, Admin(await AddAdminAsync(context)), CancellationToken.None);

        // Act
        var detail = await sut.GetAsync(onboarded.Value!.FacilityOwnerId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            detail!.Status.Should().Be(FacilityOwnerStatus.Pending.ToString());
            detail.Contracts.Single().IsLiveToday.Should().BeFalse();
        }
    }

    [Fact]
    public async Task GetAsync_ShouldReportEveryDayOfTheWeekIncludingTheClosedOnes()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var request = CreateRequest(UniqueEmail(), "Sunday Closed Courts", []) with
        {
            OperatingHours =
            [
                .. Enum.GetValues<DayOfWeek>().Select(day => day == DayOfWeek.Sunday
                    ? new OperatingHourInput(day, null, null)
                    : new OperatingHourInput(day, new TimeOnly(6, 0), new TimeOnly(22, 0)))
            ]
        };
        var onboarded = await sut.OnboardAsync(request, Admin(await AddAdminAsync(context)), CancellationToken.None);

        // Act
        var detail = await sut.GetAsync(onboarded.Value!.FacilityOwnerId, CancellationToken.None);

        // Assert
        var hours = detail!.Facilities.Single().OperatingHours;
        using (new AssertionScope())
        {
            hours.Should().HaveCount(7);
            // A closed day is a row with no times, so the console can show
            // "Closed" rather than leaving Sunday off the list entirely.
            var sunday = hours.Single(hour => hour.DayOfWeek == (int)DayOfWeek.Sunday);
            sunday.OpensAt.Should().BeNull();
            sunday.ClosesAt.Should().BeNull();
            hours.Count(hour => hour.OpensAt is not null).Should().Be(6);
        }
    }

    private static FacilityOwnerOnboardingService CreateService(AppDbContext context) => new(
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

    private static async Task<Guid> AddAdminAsync(AppDbContext context, string fullName = "Platform Admin")
    {
        var user = new User(UniqueEmail(), fullName, null);
        user.SetPasswordHash("hash");
        context.Users.Add(user);
        context.UserRoles.Add(new UserRole(user.Id, UserRoleName.PlatformAdmin));
        await context.SaveChangesAsync();
        return user.Id;
    }

    private static AuditActor Admin(Guid? userId = null) =>
        new(userId ?? Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private static string UniqueEmail() => $"detail-{Guid.NewGuid():N}@example.com";

    private static OnboardFacilityOwnerRequest CreateRequest(
        string email,
        string facilityName,
        IReadOnlyCollection<Guid> amenityIds) => new(
        new OwnerAccountInput("Juan Dela Cruz", email, "+639171234567"),
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
            amenityIds),
        [.. Enum.GetValues<DayOfWeek>().Select(day => new OperatingHourInput(
            day,
            new TimeOnly(6, 0),
            new TimeOnly(22, 0)))],
        new ContractInput(Today, Today.AddYears(1), "Signed on encoding.", SignedAgreement()));

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
