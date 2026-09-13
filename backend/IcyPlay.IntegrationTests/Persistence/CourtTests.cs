using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class CourtTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_ShouldAddTheFacilityAndTheCourtTogether()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);

        // Act
        var result = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Brand New Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Assert
        var court = await context.Courts
            .Include(candidate => candidate.Sports)
            .SingleAsync(candidate => candidate.Id == result.Value!.CourtId);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            court.FacilityOwnerId.Should().Be(owner);
            court.Sports.Should().HaveCount(2);
            court.Sports.Count(link => link.IsPrimary).Should().Be(1);
            // Follows the building unless told otherwise.
            court.UsesFacilityHours.Should().BeTrue();
            (await context.Facilities.CountAsync(facility => facility.FacilityOwnerId == owner))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldLeaveNoFacilityBehindWhenTheCourtIsRejected()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);

        // Act: a sport nobody has heard of.
        var result = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Doomed Courts"), sportIds: [Guid.NewGuid()]),
            Admin(),
            CancellationToken.None);

        // Assert: all or nothing, so an abandoned wizard leaves no stray venue.
        using (new AssertionScope())
        {
            result.Failure.Should().Be(CourtFailure.UnknownSport);
            (await context.Facilities.AnyAsync(facility => facility.Name == "Doomed Courts"))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAFacilityBelongingToAnotherOwner()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var first = await AddOwnerAsync(context);
        var second = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var theirs = await sut.CreateAsync(
            Request(second, newFacility: NewFacility("Their Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.CreateAsync(
            Request(first, facilityId: theirs.Value!.FacilityId, sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.FacilityNotFound);
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAPrimarySportThatWasNotSelected()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);

        // Act
        var result = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Mismatched Courts"), sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    PrimarySportId = Guid.NewGuid()
                }
            },
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.PrimarySportNotSelected);
    }

    [Fact]
    public async Task ListAsync_ShouldFallBackToTheFacilityHoursForACourtThatFollowsThem()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Shared Hours Courts"), sportIds: await SportIdsAsync(context, 1)),
            Admin(),
            CancellationToken.None);

        // Act
        var courts = await sut.ListAsync(created.Value!.FacilityId, CancellationToken.None);

        // Assert: the court keeps no hours of its own, so it shows the
        // building's rather than nothing at all.
        var court = courts.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            court.UsesFacilityHours.Should().BeTrue();
            court.OperatingHours.Should().HaveCount(7);
            court.Maintenance.Should().BeNull();
        }
    }

    [Fact]
    public async Task ListAsync_ShouldUseTheCourtsOwnHoursWhenItHasOptedOut()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var request = Request(owner, newFacility: NewFacility("Late Closer Courts"), sportIds: sports) with
        {
            Court = Court(sports) with
            {
                UsesFacilityHours = false,
                OperatingHours =
                [
                    .. Enum.GetValues<DayOfWeek>().Select(day => new OperatingHourInput(
                        day,
                        new TimeOnly(6, 0),
                        new TimeOnly(18, 0)))
                ]
            }
        };
        var created = await sut.CreateAsync(request, Admin(), CancellationToken.None);

        // Act
        var courts = await sut.ListAsync(created.Value!.FacilityId, CancellationToken.None);

        // Assert: the outdoor court that closes early stays expressible.
        var court = courts.Should().ContainSingle().Subject;
        using (new AssertionScope())
        {
            court.UsesFacilityHours.Should().BeFalse();
            court.OperatingHours.Should().AllSatisfy(hour =>
                hour.ClosesAt.Should().Be(new TimeOnly(18, 0)));
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldMakeTheFirstPhotoTheCoverWhenNoneWasChosen()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var request = Request(owner, newFacility: NewFacility("Photographed Courts"), sportIds: sports) with
        {
            Court = Court(sports) with
            {
                Photos =
                [
                    Photo("court-a", 1, isCover: false),
                    Photo("court-b", 2, isCover: false)
                ]
            }
        };

        // Act
        var created = await sut.CreateAsync(request, Admin(), CancellationToken.None);

        // Assert: a gallery with no cover has nothing to show in the booking
        // list, so the first picture becomes it.
        var courts = await sut.ListAsync(created.Value!.FacilityId, CancellationToken.None);
        var photos = courts.Single().Photos;

        using (new AssertionScope())
        {
            photos.Should().HaveCount(2);
            photos.Count(photo => photo.IsCover).Should().Be(1);
            photos.Single(photo => photo.IsCover).PublicId.Should().Be("court-a");
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldKeepTheCoverThatWasChosen()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var request = Request(owner, newFacility: NewFacility("Chosen Cover Courts"), sportIds: sports) with
        {
            Court = Court(sports) with
            {
                Photos =
                [
                    Photo("court-a", 1, isCover: false),
                    Photo("court-b", 2, isCover: true)
                ]
            }
        };

        // Act
        var created = await sut.CreateAsync(request, Admin(), CancellationToken.None);

        // Assert
        var photos = (await sut.ListAsync(created.Value!.FacilityId, CancellationToken.None))
            .Single().Photos;

        photos.Single(photo => photo.IsCover).PublicId.Should().Be("court-b");
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAPhotoThatIsNotOnOurOwnCloud()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var request = Request(owner, newFacility: NewFacility("Untrusted Photo Courts"), sportIds: sports) with
        {
            Court = Court(sports) with
            {
                Photos =
                [
                    Photo("court-a", 1, isCover: true) with
                    {
                        SecureUrl = "https://attacker.example/icyplay-test/image/upload/court.jpg"
                    }
                ]
            }
        };

        // Act
        var result = await sut.CreateAsync(request, Admin(), CancellationToken.None);

        // Assert: rejected before anything is written.
        using (new AssertionScope())
        {
            result.Failure.Should().Be(CourtFailure.UntrustedPhotoUrl);
            (await context.Facilities.AnyAsync(facility => facility.Name == "Untrusted Photo Courts"))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task SetFacilityMaintenanceAsync_ShouldCloseEveryCourtInside()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Renovating Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);
        await sut.CreateAsync(
            Request(owner, facilityId: created.Value!.FacilityId, sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    Name = "Court 2",
                    DisplayOrder = 20
                }
            },
            Admin(),
            CancellationToken.None);

        // Act
        await sut.SetFacilityMaintenanceAsync(
            created.Value.FacilityId,
            new SetMaintenanceRequest(Now.AddHours(-1), null, "Roof repairs"),
            Admin(),
            CancellationToken.None);

        // Assert: closing the building closes what is in it.
        var courts = await sut.ListAsync(created.Value.FacilityId, CancellationToken.None);
        using (new AssertionScope())
        {
            courts.Should().HaveCount(2);
            courts.Should().AllSatisfy(court =>
            {
                court.Maintenance.Should().NotBeNull();
                // Said out loud, because it cannot be lifted from the court.
                court.Maintenance!.AppliesToWholeFacility.Should().BeTrue();
                court.Maintenance.Reason.Should().Be("Roof repairs");
            });
        }
    }

    [Fact]
    public async Task SetCourtMaintenanceAsync_ShouldCloseOnlyThatCourt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var first = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("One Down Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);
        await sut.CreateAsync(
            Request(owner, facilityId: first.Value!.FacilityId, sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    Name = "Court 2",
                    DisplayOrder = 20
                }
            },
            Admin(),
            CancellationToken.None);

        // Act
        await sut.SetCourtMaintenanceAsync(
            first.Value.CourtId,
            new SetMaintenanceRequest(Now.AddHours(-1), Now.AddDays(3), "Net replacement"),
            Admin(),
            CancellationToken.None);

        // Assert
        var courts = await sut.ListAsync(first.Value.FacilityId, CancellationToken.None);
        using (new AssertionScope())
        {
            courts.Single(court => court.Id == first.Value.CourtId)
                .Maintenance!.AppliesToWholeFacility.Should().BeFalse();
            courts.Single(court => court.Id != first.Value.CourtId)
                .Maintenance.Should().BeNull();
        }
    }

    [Fact]
    public async Task SetCourtMaintenanceAsync_ShouldRefuseASecondLiveClosure()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Twice Closed Courts"), sportIds: await SportIdsAsync(context, 1)),
            Admin(),
            CancellationToken.None);
        await sut.SetCourtMaintenanceAsync(
            created.Value!.CourtId,
            new SetMaintenanceRequest(Now, null, "First reason"),
            Admin(),
            CancellationToken.None);

        // Act
        var second = await sut.SetCourtMaintenanceAsync(
            created.Value.CourtId,
            new SetMaintenanceRequest(Now, null, "Second reason"),
            Admin(),
            CancellationToken.None);

        // Assert: two reasons on one badge helps nobody.
        second.Failure.Should().Be(CourtFailure.AlreadyUnderMaintenance);
    }

    [Fact]
    public async Task LiftMaintenanceAsync_ShouldReopenTheCourtAndKeepTheRecord()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Reopened Courts"), sportIds: await SportIdsAsync(context, 1)),
            Admin(),
            CancellationToken.None);
        var period = await sut.SetCourtMaintenanceAsync(
            created.Value!.CourtId,
            new SetMaintenanceRequest(Now.AddHours(-1), null, "Floor drying"),
            Admin(),
            CancellationToken.None);

        // Act
        await sut.LiftMaintenanceAsync(period.Value, Admin(), CancellationToken.None);

        // Assert
        var courts = await sut.ListAsync(created.Value.FacilityId, CancellationToken.None);
        using (new AssertionScope())
        {
            courts.Single().Maintenance.Should().BeNull();
            // The closure stops applying; the row stays, so the history holds.
            (await context.MaintenancePeriods.CountAsync()).Should().BeGreaterThan(0);
            (await context.AuditLogs.AnyAsync(entry => entry.Action == AuditAction.MaintenanceLifted))
                .Should().BeTrue();
        }
    }

    private const string CloudName = "icyplay-test";

    private static CourtService CreateService(AppDbContext context) => new(
        context,
        new AuditLogger(context, new FixedTimeProvider(Now)),
        new CloudinaryAssetService(
            Options.Create(new CloudinaryOptions
            {
                CloudName = CloudName,
                ApiKey = "123456789012345",
                ApiSecret = "test-api-secret"
            }),
            new FixedTimeProvider(Now)),
        new FixedTimeProvider(Now),
        NullLogger<CourtService>.Instance);

    private static async Task<Guid> AddOwnerAsync(AppDbContext context)
    {
        var user = new User($"court-{Guid.NewGuid():N}@example.com", "Court Owner", null);
        user.SetPasswordHash("hash");
        context.Users.Add(user);
        var owner = new FacilityOwner(user.Id, "Court Ventures", "billing@example.com", null);
        context.FacilityOwners.Add(owner);
        await context.SaveChangesAsync();
        return owner.Id;
    }

    private static async Task<Guid[]> SportIdsAsync(AppDbContext context, int count) =>
        await context.Sports
            .Where(sport => sport.IsActive)
            .OrderBy(sport => sport.Key)
            .Take(count)
            .Select(sport => sport.Id)
            .ToArrayAsync();

    private static PhotoInput Photo(string publicId, int order, bool isCover) => new(
        publicId,
        $"https://res.cloudinary.com/{CloudName}/image/upload/v1/{publicId}.jpg",
        null,
        order,
        isCover);

    private static AuditActor Admin() => new(Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private static CreateCourtRequest Request(
        Guid ownerId,
        IReadOnlyCollection<Guid> sportIds,
        Guid? facilityId = null,
        NewFacilityInput? newFacility = null) =>
        new(ownerId, facilityId, newFacility, Court(sportIds));

    private static CourtInput Court(IReadOnlyCollection<Guid> sportIds) => new(
        "Court 1",
        10,
        "The near court.",
        sportIds,
        sportIds.First(),
        CourtVenueType.Covered,
        CourtSurface.Concrete,
        true,
        "Full court",
        12,
        "Net provided",
        60,
        60,
        0,
        true,
        [],
        []);

    private static NewFacilityInput NewFacility(string name) => new(
        new FacilityInput(
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
            []),
        [
            .. Enum.GetValues<DayOfWeek>().Select(day => new OperatingHourInput(
                day,
                new TimeOnly(6, 0),
                new TimeOnly(22, 0)))
        ],
        []);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
