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
using Microsoft.Extensions.Caching.Memory;
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

    private static CourtService CreateService(AppDbContext context) =>
        CreateService(context, CreateCatalog(context));

    private static CourtService CreateService(AppDbContext context, IActivityCatalog catalog) => new(
        context,
        catalog,
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

    private static async Task<Guid> SportIdAsync(AppDbContext context, string key) =>
        await context.Sports.Where(sport => sport.Key == key).Select(sport => sport.Id).SingleAsync();

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

    [Fact]
    public async Task GetAsync_ShouldReadOneCourtTheSameWayTheListDoes()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Readable Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var court = await sut.GetAsync(created.Value!.CourtId, CancellationToken.None);

        // Assert: a detail page that disagrees with the list it came from is
        // worse than no detail page.
        var fromList = (await sut.ListAsync(created.Value.FacilityId, CancellationToken.None)).Single();

        using (new AssertionScope())
        {
            court.Should().NotBeNull();
            court.Should().BeEquivalentTo(fromList);
            court!.FacilityOwnerId.Should().Be(owner);
        }
    }

    [Fact]
    public async Task GetAsync_ShouldReturnNullWhenNoCourtHasThatId()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act
        var court = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        court.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldMoveThePrimarySportWithoutChurningTheLinkThatStays()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 3);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Movable Courts"), sportIds: sports.Take(2).ToArray()),
            Admin(),
            CancellationToken.None);

        var kept = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == created.Value!.CourtId && link.SportId == sports[1])
            .Select(link => link.Id)
            .SingleAsync();

        // Act: the first sport goes, a third arrives, and the second becomes
        // the main one.
        var result = await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(
                Court([sports[1], sports[2]]) with
                {
                    PrimarySportId = sports[1]
                },
                IsActive: true,
                "Owner changed what it takes"),
            Admin(),
            CancellationToken.None);

        // Assert
        var links = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == created.Value.CourtId)
            .ToListAsync();

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            links.Select(link => link.SportId).Should().BeEquivalentTo([sports[1], sports[2]]);
            links.Should().ContainSingle(link => link.IsPrimary)
                .Which.SportId.Should().Be(sports[1]);
            // The surviving link keeps its row, and with it the record of when
            // this sport was first put on this court.
            links.Should().Contain(link => link.Id == kept);
        }
    }

    [Fact]
    public async Task UpdateAsync_ShouldSetOwnHoursInPlaceWhenTheCourtStopsFollowingTheFacility()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Late Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        var ownHours = Enum.GetValues<DayOfWeek>()
            .Select(day => new OperatingHourInput(day, new TimeOnly(8, 0), new TimeOnly(20, 0)))
            .ToArray();

        // Act: twice, because one row per day is unique and the second save is
        // what a delete-then-insert would collide on.
        await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(
                Court(sports) with
                {
                    UsesFacilityHours = false,
                    OperatingHours = ownHours
                },
                IsActive: true,
                null),
            Admin(),
            CancellationToken.None);

        var result = await sut.UpdateAsync(
            created.Value.CourtId,
            new UpdateCourtRequest(
                Court(sports) with
                {
                    UsesFacilityHours = false,
                    OperatingHours = ownHours
                },
                IsActive: true,
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        var hours = await context.CourtOperatingHours
            .AsNoTracking()
            .Where(hour => hour.CourtId == created.Value.CourtId)
            .ToListAsync();

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            hours.Should().HaveCount(7);
            hours.Should().OnlyContain(hour => hour.OpensAt == new TimeOnly(8, 0));
        }
    }

    [Fact]
    public async Task UpdateAsync_ShouldRefuseAPrimarySportThatIsNotSelected()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Mismatched Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(
                Court([sports[0]]) with
                {
                    PrimarySportId = sports[1]
                },
                IsActive: true,
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.PrimarySportNotSelected);
    }

    [Fact]
    public async Task UpdateAsync_ShouldRefuseAPhotoUrlFromAnotherHost()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Spoofable Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(
                Court(sports) with
                {
                    Photos = [new PhotoInput("evil", "https://evil.example.com/a.jpg", null, 1, true)]
                },
                IsActive: true,
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Failure.Should().Be(CourtFailure.UntrustedPhotoUrl);
            (await context.Photos.CountAsync(photo => photo.CourtId == created.Value.CourtId))
                .Should().Be(0);
        }
    }

    [Fact]
    public async Task UpdateAsync_ShouldTakeTheCourtOffTheBookingPortalWhenDeactivated()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Closable Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(Court(sports), IsActive: false, "Resurfacing for good"),
            Admin(),
            CancellationToken.None);

        // Assert
        var court = await context.Courts.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == created.Value.CourtId);

        court.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldReturnNotFoundForACourtThatDoesNotExist()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var sports = await SportIdsAsync(context, 1);

        // Act
        var result = await sut.UpdateAsync(
            Guid.NewGuid(),
            new UpdateCourtRequest(Court(sports), IsActive: true, null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.CourtNotFound);
    }

    [Fact]
    public async Task CreateAsync_ShouldDivideTheCourtPerSport()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);

        // Act: played whole for the first sport, three across for the second.
        var result = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Divisible Courts"), sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    Sports =
                    [
                        new CourtSportInput(sports[0], 1),
                        new CourtSportInput(sports[1], 3)
                    ]
                }
            },
            Admin(),
            CancellationToken.None);

        // Assert
        var links = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == result.Value!.CourtId)
            .ToListAsync();

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            links.Single(link => link.SportId == sports[0]).Divisions.Should().Be(1);
            links.Single(link => link.SportId == sports[1]).Divisions.Should().Be(3);
            links.Single(link => link.SportId == sports[1]).IsDivided.Should().BeTrue();
        }
    }

    [Fact]
    public async Task UpdateAsync_ShouldChangeHowManyCourtsASportMakes()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Remarked Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act: the hall gets marked out into four.
        await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(Court(sports, divisions: 4), IsActive: true, "Re-marked the floor"),
            Admin(),
            CancellationToken.None);

        // Assert
        var link = await context.CourtSports
            .AsNoTracking()
            .SingleAsync(candidate => candidate.CourtId == created.Value.CourtId);

        link.Divisions.Should().Be(4);
    }

    [Fact]
    public async Task UpdateDivisionsAsync_ShouldRemarkOneSportWithoutTouchingTheRest()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Marked Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act: only the second sport is re-marked.
        var result = await sut.UpdateDivisionsAsync(
            created.Value!.CourtId,
            new UpdateCourtDivisionsRequest(
                [new CourtSportInput(sports[1], 3)],
                "Floor re-marked for pickleball"),
            Admin(),
            CancellationToken.None);

        // Assert
        var links = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == created.Value.CourtId)
            .ToListAsync();

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            links.Single(link => link.SportId == sports[1]).Divisions.Should().Be(3);
            // Untouched, because it was not sent.
            links.Single(link => link.SportId == sports[0]).Divisions.Should().Be(1);
        }
    }

    [Fact]
    public async Task UpdateDivisionsAsync_ShouldRefuseASportThisCourtDoesNotTake()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 3);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Unmarked Courts"), sportIds: sports.Take(2).ToArray()),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.UpdateDivisionsAsync(
            created.Value!.CourtId,
            new UpdateCourtDivisionsRequest([new CourtSportInput(sports[2], 2)], null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.UnknownSport);
    }

    [Fact]
    public async Task UpdateDivisionsAsync_ShouldKeepThePriceOnASportThatIsRemarked()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Priced Marked Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, null, null, null)],
                null,
                null),
            Admin(),
            CancellationToken.None);

        // Act
        await sut.UpdateDivisionsAsync(
            created.Value.CourtId,
            new UpdateCourtDivisionsRequest([new CourtSportInput(sports[0], 3)], null),
            Admin(),
            CancellationToken.None);

        // Assert: re-marking a floor is not a reason to make its owner retype a
        // rate card.
        var link = await context.CourtSports
            .AsNoTracking()
            .SingleAsync(candidate => candidate.CourtId == created.Value.CourtId);

        using (new AssertionScope())
        {
            link.Divisions.Should().Be(3);
            link.StandardHourlyRate.Should().Be(500m);
        }
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldPriceEachSportOnItsOwn()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Priced Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act: an hour of one sport is not worth an hour of the other.
        var result = await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [
                    new SportPricingInput(sports[0], 500m, 750m, 600m, 900m),
                    new SportPricingInput(sports[1], 350m, null, null, null)
                ],
                new PeakWindowInput(new TimeOnly(18, 0), new TimeOnly(22, 0), true, false),
                "Owner sent the rate card"),
            Admin(),
            CancellationToken.None);

        // Assert
        var links = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == created.Value.CourtId)
            .ToListAsync();

        var first = links.Single(link => link.SportId == sports[0]);
        var second = links.Single(link => link.SportId == sports[1]);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            first.StandardHourlyRate.Should().Be(500m);
            first.PeakHourlyRate.Should().Be(750m);
            first.HolidayRate.Should().Be(900m);
            second.StandardHourlyRate.Should().Be(350m);
            // A venue charging the same all week stores one number, not four
            // copies of it.
            second.PeakHourlyRate.Should().BeNull();
            second.RateFor(CourtRateKind.Peak).Should().Be(350m);
            second.RateFor(CourtRateKind.Holiday).Should().Be(350m);
        }
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldStoreWhenThePeakRateApplies()
    {
        // Arrange: the facility opens 06:00 and closes 22:00 every day.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Evening Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act: busy on weekday evenings, ordinary at the weekend.
        var result = await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, 750m, null, null)],
                new PeakWindowInput(new TimeOnly(18, 0), new TimeOnly(22, 0), true, false),
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        var court = await context.Courts.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == created.Value.CourtId);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            court.PeakStartsAt.Should().Be(new TimeOnly(18, 0));
            court.PeakOnWeekdays.Should().BeTrue();
            court.PeakOnWeekends.Should().BeFalse();
            court.IsPeakAt(DayOfWeek.Tuesday, new TimeOnly(19, 0)).Should().BeTrue();
            court.IsPeakAt(DayOfWeek.Tuesday, new TimeOnly(17, 0)).Should().BeFalse();
            // The same hour, but the window does not run at the weekend.
            court.IsPeakAt(DayOfWeek.Saturday, new TimeOnly(19, 0)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldRefuseAPeakWindowThatRunsPastClosingTime()
    {
        // Arrange: the facility closes at 22:00.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Overrun Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, 750m, null, null)],
                new PeakWindowInput(new TimeOnly(20, 0), new TimeOnly(23, 30), true, true),
                null),
            Admin(),
            CancellationToken.None);

        // Assert: an hour nobody can book is not an hour anybody can be charged
        // a premium for.
        result.Failure.Should().Be(CourtFailure.PeakWindowOutsideHours);
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldRefuseAPeakWindowBeforeOpeningTime()
    {
        // Arrange: the facility opens at 06:00.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Early Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, 750m, null, null)],
                new PeakWindowInput(new TimeOnly(5, 0), new TimeOnly(9, 0), true, true),
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.PeakWindowOutsideHours);
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldRefuseAPeakWindowOnDaysTheCourtIsShut()
    {
        // Arrange: open on weekdays only.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Weekday Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        await sut.UpdateAsync(
            created.Value!.CourtId,
            new UpdateCourtRequest(
                Court(sports) with
                {
                    UsesFacilityHours = false,
                    OperatingHours =
                    [
                        .. Enum.GetValues<DayOfWeek>().Select(day =>
                            new OperatingHourInput(
                                day,
                                day is DayOfWeek.Saturday or DayOfWeek.Sunday ? null : new TimeOnly(6, 0),
                                day is DayOfWeek.Saturday or DayOfWeek.Sunday ? null : new TimeOnly(22, 0)))
                    ]
                },
                IsActive: true,
                null),
            Admin(),
            CancellationToken.None);

        // Act
        var result = await sut.UpdatePricingAsync(
            created.Value.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, 750m, null, null)],
                new PeakWindowInput(new TimeOnly(18, 0), new TimeOnly(21, 0), false, true),
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.PeakWindowOnClosedDays);
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldClearTheWindowWhenNoSportChargesAPeakRate()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Flattened Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, 750m, null, null)],
                new PeakWindowInput(new TimeOnly(18, 0), new TimeOnly(22, 0), true, true),
                null),
            Admin(),
            CancellationToken.None);

        // Act: the peak rate is taken away again.
        await sut.UpdatePricingAsync(
            created.Value.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[0], 500m, null, null, null)],
                null,
                null),
            Admin(),
            CancellationToken.None);

        // Assert: a window nobody is charged for would read as a live rule the
        // next time someone opened the screen.
        var court = await context.Courts.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == created.Value.CourtId);

        using (new AssertionScope())
        {
            court.HasPeakWindow.Should().BeFalse();
            court.PeakOnWeekdays.Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldLeaveSportsThatWereNotSentAlone()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Partly Priced Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [
                    new SportPricingInput(sports[0], 500m, null, null, null),
                    new SportPricingInput(sports[1], 350m, null, null, null)
                ],
                null,
                null),
            Admin(),
            CancellationToken.None);

        // Act: only one sport is corrected.
        await sut.UpdatePricingAsync(
            created.Value.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[1], 400m, null, null, null)],
                null,
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        var links = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == created.Value.CourtId)
            .ToListAsync();

        using (new AssertionScope())
        {
            links.Single(link => link.SportId == sports[0]).StandardHourlyRate.Should().Be(500m);
            links.Single(link => link.SportId == sports[1]).StandardHourlyRate.Should().Be(400m);
        }
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldRefuseASportThisCourtDoesNotTake()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 3);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Narrow Courts"), sportIds: sports.Take(2).ToArray()),
            Admin(),
            CancellationToken.None);

        // Act: a price for a sport that is not on this court would sit in the
        // table unreachable, and be silently wrong if the sport is added later.
        var result = await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[2], 500m, null, null, null)],
                null,
                null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.UnknownSport);
    }

    [Fact]
    public async Task UpdatePricingAsync_ShouldReturnNotFoundForACourtThatDoesNotExist()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act
        var result = await sut.UpdatePricingAsync(
            Guid.NewGuid(),
            new UpdateCourtPricingRequest([], null, null),
            Admin(),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.CourtNotFound);
    }

    [Fact]
    public async Task UpdateAsync_ShouldKeepThePriceOnASportThatSurvivesTheEdit()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 3);
        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Repriced Courts"), sportIds: sports.Take(2).ToArray()),
            Admin(),
            CancellationToken.None);

        await sut.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(sports[1], 500m, null, null, null)],
                null,
                null),
            Admin(),
            CancellationToken.None);

        // Act: the sports change around the one that was priced.
        await sut.UpdateAsync(
            created.Value.CourtId,
            new UpdateCourtRequest(
                Court([sports[1], sports[2]]) with
                {
                    PrimarySportId = sports[1]
                },
                IsActive: true,
                null),
            Admin(),
            CancellationToken.None);

        // Assert: editing a court is not a reason to make its owner retype a
        // rate card.
        var link = await context.CourtSports
            .AsNoTracking()
            .SingleAsync(candidate =>
                candidate.CourtId == created.Value.CourtId && candidate.SportId == sports[1]);

        link.StandardHourlyRate.Should().Be(500m);
    }

    [Fact]
    public async Task Sports_ShouldBeSeededWithEventsAlongsideTheGames()
    {
        // Arrange
        await using var context = database.CreateContext();

        // Act
        var entries = await context.Sports.AsNoTracking().ToListAsync();

        // Assert: a court is hired for occasions as well as played on, and the
        // two are told apart by kind rather than by reading the category.
        using (new AssertionScope())
        {
            entries.Should().Contain(entry =>
                entry.Name == "Birthday party" && entry.Kind == ActivityKind.Event);
            entries.Should().Contain(entry =>
                entry.Name == "Basketball" && entry.Kind == ActivityKind.Sport);
            entries.Where(entry => entry.Kind == ActivityKind.Event)
                .Should().OnlyContain(entry => entry.Category == SportCategory.Events);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldLetACourtBeBookedForAnEvent()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var party = await context.Sports
            .Where(entry => entry.Kind == ActivityKind.Event)
            .Select(entry => entry.Id)
            .FirstAsync();

        // Act: an occasion takes the whole floor, so it is one court.
        var result = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Function Courts"), sportIds: [party]),
            Admin(),
            CancellationToken.None);

        // Assert
        var court = await sut.GetAsync(result.Value!.CourtId, CancellationToken.None);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            court!.Sports.Single().Kind.Should().Be(ActivityKind.Event);
            court.Sports.Single().Divisions.Should().Be(1);
        }
    }

    [Fact]
    public async Task Catalog_ShouldOfferOnlyWhatHasACourtConfigured()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);

        await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Catalogued Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var offered = await catalog.ListAsync(CancellationToken.None);

        // Assert: a filter that returns nothing is worse than one never offered,
        // so only what somebody can actually book appears.
        using (new AssertionScope())
        {
            offered.Should().Contain(activity => activity.Id == sports[0]);
            offered.Should().OnlyContain(activity => activity.CourtCount > 0);
            var seededWithNoCourt = await context.Sports
                .Where(sport => sport.Key == "squash")
                .Select(sport => sport.Id)
                .SingleAsync();
            offered.Should().NotContain(activity => activity.Id == seededWithNoCourt);
        }
    }

    [Fact]
    public async Task Catalog_ShouldCountEachDivisionAsACourtToBook()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);

        var before = (await catalog.ListAsync(CancellationToken.None))
            .FirstOrDefault(activity => activity.Id == sports[0])?.CourtCount ?? 0;

        await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Divided Catalogue Courts"), sportIds: sports) with
            {
                Court = Court(sports, divisions: 3)
            },
            Admin(),
            CancellationToken.None);
        catalog.Invalidate();

        // Act
        var offered = await catalog.ListAsync(CancellationToken.None);

        // Assert: a floor marked out into three is three courts to book, and
        // saying "one" would undersell the venue.
        offered.Single(activity => activity.Id == sports[0]).CourtCount
            .Should().Be(before + 3);
    }

    [Fact]
    public async Task Catalog_ShouldServeTheSameAnswerUntilItIsCleared()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);

        var before = await catalog.ListAsync(CancellationToken.None);

        await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Uncached Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act: this catalogue was never told, so it still holds the old answer.
        var stillCached = await catalog.ListAsync(CancellationToken.None);
        catalog.Invalidate();
        var afterClearing = await catalog.ListAsync(CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            stillCached.Should().BeEquivalentTo(before);
            afterClearing.Should().Contain(activity => activity.Id == sports[0]);
        }
    }

    [Fact]
    public async Task Catalog_ShouldListEveryPartOfADividedCourtSeparately()
    {
        // Arrange: one floor, marked out three ways for this sport.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);
        var sportKey = await context.Sports
            .Where(sport => sport.Id == sports[0])
            .Select(sport => sport.Key)
            .SingleAsync();

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Sliced Listing Courts"), sportIds: sports) with
            {
                Court = Court(sports, divisions: 3) with
                {
                    Name = "Hall A"
                }
            },
            Admin(),
            CancellationToken.None);

        // Act
        var listed = await catalog.ListCourtsAsync(sportKey, CancellationToken.None);
        var parts = listed.Where(court => court.CourtId == created.Value!.CourtId).ToArray();

        // Assert: three games can run at once, so a customer is offered three
        // courts rather than one.
        using (new AssertionScope())
        {
            parts.Should().HaveCount(3);
            parts.Select(court => court.DivisionNumber).Should().BeEquivalentTo([1, 2, 3]);
            parts.Select(court => court.Name).Should().OnlyHaveUniqueItems();
            parts.Should().OnlyContain(court => court.FacilityName == "Sliced Listing Courts");
        }
    }

    [Fact]
    public async Task Catalog_ShouldListACourtPlayedWholeUnderItsOwnName()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);
        var sportKey = await context.Sports
            .Where(sport => sport.Id == sports[0])
            .Select(sport => sport.Key)
            .SingleAsync();

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Whole Listing Courts"), sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    Name = "Main Court"
                }
            },
            Admin(),
            CancellationToken.None);

        // Act
        var listed = await catalog.ListCourtsAsync(sportKey, CancellationToken.None);
        var court = listed.Single(candidate => candidate.CourtId == created.Value!.CourtId);

        // Assert: numbering one of one only invites the question of where the
        // second is.
        using (new AssertionScope())
        {
            court.Name.Should().Be("Main Court");
            court.DivisionNumber.Should().Be(1);
        }
    }

    [Fact]
    public async Task Catalog_ShouldListACourtOncePerSportItIsSetUpFor()
    {
        // Arrange: one floor, three sports, and the middle one marked out twice.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 3);

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Many Sports Courts"), sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    Sports =
                    [
                        new CourtSportInput(sports[0], 1),
                        new CourtSportInput(sports[1], 2),
                        new CourtSportInput(sports[2], 1)
                    ]
                }
            },
            Admin(),
            CancellationToken.None);

        // Act: nothing named, so everything on offer.
        var listed = await catalog.ListCourtsAsync(null, CancellationToken.None);
        var mine = listed.Where(court => court.CourtId == created.Value!.CourtId).ToArray();

        // Assert: one offering per sport, and the divided one twice. Each is
        // priced on its own, so the sport has to be on the row or they read as
        // duplicates.
        using (new AssertionScope())
        {
            mine.Should().HaveCount(4);
            mine.Select(court => court.SportKey).Distinct().Should().HaveCount(3);
            mine.Should().OnlyContain(court => court.SportName.Length > 0);
        }
    }

    [Fact]
    public async Task Catalog_ShouldOfferNoCourtsForASportNobodyHasSetUp()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);

        // Act
        var listed = await catalog.ListCourtsAsync("squash", CancellationToken.None);

        // Assert: the page can say so plainly rather than showing an empty grid.
        listed.Should().BeEmpty();
    }

    [Fact]
    public async Task ListInventoryAsync_ShouldNarrowToOneFacilityWithoutLosingTheRest()
    {
        // Arrange: one owner, two venues, a court in each.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);

        var first = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Inventory Venue One"), sportIds: sports),
            Admin(),
            CancellationToken.None);
        var second = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Inventory Venue Two"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var everything = await sut.ListInventoryAsync(
            new CourtInventoryQuery(FacilityOwnerId: owner, PageSize: 100),
            CancellationToken.None);
        var justOne = await sut.ListInventoryAsync(
            new CourtInventoryQuery(FacilityId: first.Value!.FacilityId, PageSize: 100),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            everything.Items.Should().HaveCount(2);
            everything.Items.Should().OnlyContain(court => court.FacilityOwnerId == owner);
            justOne.Items.Should().ContainSingle()
                .Which.FacilityId.Should().Be(first.Value.FacilityId);
            justOne.Items.Should().NotContain(court => court.Id == second.Value!.CourtId);
        }
    }

    [Fact]
    public async Task ListInventoryAsync_ShouldCountEveryDivisionAsSomethingToBook()
    {
        // Arrange: one floor, two sports, the second marked out three ways.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 2);

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Counted Courts"), sportIds: sports) with
            {
                Court = Court(sports) with
                {
                    Sports =
                    [
                        new CourtSportInput(sports[0], 1),
                        new CourtSportInput(sports[1], 3)
                    ]
                }
            },
            Admin(),
            CancellationToken.None);

        // Act
        var listed = await sut.ListInventoryAsync(
            new CourtInventoryQuery(FacilityId: created.Value!.FacilityId),
            CancellationToken.None);

        // Assert: saying "one court" would undersell what the venue can take.
        listed.Items.Single().BookableUnits.Should().Be(4);
    }

    [Fact]
    public async Task ListInventoryAsync_ShouldFindACourtByTheVenueItIsIn()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var owner = await AddOwnerAsync(context);
        var sports = await SportIdsAsync(context, 1);
        var name = $"Searchable Venue {Guid.NewGuid():N}";

        await sut.CreateAsync(
            Request(owner, newFacility: NewFacility(name), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act: an admin types the venue, not the court.
        var found = await sut.ListInventoryAsync(
            new CourtInventoryQuery(Search: name),
            CancellationToken.None);

        // Assert
        found.Items.Should().ContainSingle().Which.FacilityName.Should().Be(name);
    }

    [Fact]
    public async Task Catalog_ShouldReadOneCourtWithTheVenueAroundIt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);
        var sportKey = await context.Sports
            .Where(sport => sport.Id == sports[0])
            .Select(sport => sport.Key)
            .SingleAsync();

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Detailed Venue"), sportIds: sports) with
            {
                Court = Court(sports, divisions: 2)
            },
            Admin(),
            CancellationToken.None);

        // Act
        var detail = await catalog.GetCourtAsync(
            created.Value!.CourtId,
            sportKey,
            2,
            CancellationToken.None);

        // Assert: one read, because a page assembled from five calls shows five
        // different moments.
        using (new AssertionScope())
        {
            detail.Should().NotBeNull();
            detail!.Court.DivisionNumber.Should().Be(2);
            detail.Court.FacilityName.Should().Be("Detailed Venue");
            detail.Venue.HouseRules.Should().Be("No street shoes on the court.");
            detail.Venue.SafetyMeasures.Should().Be("First aid kit on site.");
            // Seven days, resolved from whichever level the court follows.
            detail.Venue.OperatingHours.Should().HaveCount(7);
        }
    }

    [Fact]
    public async Task Catalog_ShouldNotReadADivisionThatDoesNotExist()
    {
        // Arrange: a court played whole, so there is no second part.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var catalog = CreateCatalog(context);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);
        var sportKey = await context.Sports
            .Where(sport => sport.Id == sports[0])
            .Select(sport => sport.Key)
            .SingleAsync();

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Whole Detail Venue"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        // Act
        var detail = await catalog.GetCourtAsync(
            created.Value!.CourtId,
            sportKey,
            2,
            CancellationToken.None);

        // Assert: a typed URL cannot invent a court.
        detail.Should().BeNull();
    }

    [Fact]
    public async Task Catalog_ShouldShowACourtReopeningAsSoonAsMaintenanceIsLifted()
    {
        // Arrange: a court, closed, and read while it is closed so there is a
        // cached answer to go stale.
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateService(context, catalog);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);
        var sportKey = await context.Sports
            .Where(sport => sport.Id == sports[0])
            .Select(sport => sport.Key)
            .SingleAsync();

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Reopening Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        var closure = await sut.SetCourtMaintenanceAsync(
            created.Value!.CourtId,
            new SetMaintenanceRequest(Now.AddHours(-1), null, "Resurfacing"),
            Admin(),
            CancellationToken.None);

        var whileShut = await catalog.ListCourtsAsync(sportKey, CancellationToken.None);

        // Act
        await sut.LiftMaintenanceAsync(closure.Value, Admin(), CancellationToken.None);
        var afterLifting = await catalog.ListCourtsAsync(sportKey, CancellationToken.None);

        // Assert: a reopened court still reading as shut turns customers away
        // from a court that is free.
        using (new AssertionScope())
        {
            whileShut.Single(court => court.CourtId == created.Value.CourtId)
                .IsUnderMaintenance.Should().BeTrue();
            afterLifting.Single(court => court.CourtId == created.Value.CourtId)
                .IsUnderMaintenance.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Catalog_ShouldShowAClosureAsSoonAsItIsSet()
    {
        // Arrange
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateService(context, catalog);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);
        var sports = await SportIdsAsync(context, 1);
        var sportKey = await context.Sports
            .Where(sport => sport.Id == sports[0])
            .Select(sport => sport.Key)
            .SingleAsync();

        var created = await sut.CreateAsync(
            Request(owner, newFacility: NewFacility("Closing Courts"), sportIds: sports),
            Admin(),
            CancellationToken.None);

        var whileOpen = await catalog.ListCourtsAsync(sportKey, CancellationToken.None);

        // Act: the whole venue is shut, which takes every court in it down.
        await sut.SetFacilityMaintenanceAsync(
            created.Value!.FacilityId,
            new SetMaintenanceRequest(Now.AddHours(-1), Now.AddDays(3), "Storm damage"),
            Admin(),
            CancellationToken.None);

        var afterClosing = await catalog.ListCourtsAsync(sportKey, CancellationToken.None);

        // Assert: the other way round is worse — a booking taken for a court
        // nobody can get into.
        var court = afterClosing.Single(candidate => candidate.CourtId == created.Value.CourtId);

        using (new AssertionScope())
        {
            whileOpen.Single(candidate => candidate.CourtId == created.Value.CourtId)
                .IsUnderMaintenance.Should().BeFalse();
            court.IsUnderMaintenance.Should().BeTrue();
            court.WholeVenueClosed.Should().BeTrue();
            court.MaintenanceEndsAt.Should().Be(Now.AddDays(3));
        }
    }

    [Fact]
    public async Task Catalog_ShouldListOneCourtSetUpThreeWaysAsFiveBookableCourts()
    {
        // Arrange: one floor, sold three ways -- basketball, volleyball, and
        // pickleball marked out three across.
        await using var context = database.CreateContext();
        var catalog = CreateCatalog(context);
        var sut = CreateService(context, catalog);
        var owner = await AddOwnerAsync(context);
        await AddLiveContractAsync(context, owner);

        var basketball = await SportIdAsync(context, "basketball");
        var volleyball = await SportIdAsync(context, "volleyball");
        var pickleball = await SportIdAsync(context, "pickleball");

        var created = await sut.CreateAsync(
            Request(
                owner,
                newFacility: NewFacility("Five Unit Courts"),
                sportIds: [basketball, volleyball, pickleball]) with
            {
                Court = Court([basketball, volleyball, pickleball]) with
                {
                    Name = "Che court 1",
                    Sports =
                    [
                        new CourtSportInput(basketball, 1),
                        new CourtSportInput(volleyball, 1),
                        new CourtSportInput(pickleball, 3)
                    ]
                }
            },
            Admin(),
            CancellationToken.None);

        // Act
        var listed = await catalog.ListCourtsAsync(null, CancellationToken.None);
        var mine = listed.Where(court => court.CourtId == created.Value!.CourtId).ToArray();

        // Assert: five things a customer can book, in a fixed order.
        //
        // Pinned exactly rather than counted, because the listing is about to
        // stop deriving these rows and start reading them from a table. A count
        // would still pass if the names or the order moved, and the order is
        // what a visitor sees.
        using (new AssertionScope())
        {
            mine.Select(court => court.Name).Should().Equal(
                "Che court 1",
                "Che court 1 \u00b7 Pickleball 1",
                "Che court 1 \u00b7 Pickleball 2",
                "Che court 1 \u00b7 Pickleball 3",
                "Che court 1");
            mine.Select(court => court.SportName).Should().Equal(
                "Basketball",
                "Pickleball",
                "Pickleball",
                "Pickleball",
                "Volleyball");
            mine.Select(court => court.DivisionNumber).Should().Equal(1, 1, 2, 3, 1);
        }
    }

    [Fact]
    public async Task Roster_ShouldSellOneFloorAsFiveCourtsWhenItIsSetUpThreeWays()
    {
        // Arrange, Act
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var court = await ThreeWayCourtAsync(context, sut, "Roster Five Courts");

        // Assert: basketball whole, volleyball whole, pickleball three across.
        // What the venue can sell at once, which is not the same as how many
        // floors it has.
        var units = await UnitsAsync(context, court.CourtId);

        using (new AssertionScope())
        {
            units.Should().HaveCount(5);
            units.Count(unit => unit.Kind == BookableCourtKind.Whole).Should().Be(2);
            units.Count(unit => unit.Kind == BookableCourtKind.Divided).Should().Be(3);
            units.Should().OnlyContain(unit => unit.IsActive);
            units.Where(unit => unit.CourtSportId == court.PickleballLinkId)
                .Select(unit => unit.DivisionNumber)
                .Should().BeEquivalentTo([1, 2, 3]);
        }
    }

    [Fact]
    public async Task Roster_ShouldKeepTheExistingPartsWhenTheFloorIsMarkedIntoMore()
    {
        // Arrange: three pickleball courts, and a note of which rows they are.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var court = await ThreeWayCourtAsync(context, sut, "Roster Widening Courts");
        var before = (await PartsAsync(context, court.PickleballLinkId))
            .ToDictionary(unit => unit.DivisionNumber, unit => unit.Id);

        // Act
        await sut.UpdateDivisionsAsync(
            court.CourtId,
            new UpdateCourtDivisionsRequest(
                [new CourtSportInput(court.Pickleball, 5)],
                "Re-marked wider"),
            Admin(),
            CancellationToken.None);

        // Assert: parts one to three are the same rows they were. Renumbering
        // them would move every booking already taken on this floor.
        var after = await PartsAsync(context, court.PickleballLinkId);

        using (new AssertionScope())
        {
            after.Should().HaveCount(5);
            after.Select(unit => unit.DivisionNumber).Should().BeEquivalentTo([1, 2, 3, 4, 5]);

            foreach (var (number, id) in before)
            {
                after.Single(unit => unit.DivisionNumber == number).Id.Should().Be(id);
            }
        }
    }

    [Fact]
    public async Task Roster_ShouldRetireAPartRatherThanDeleteItWhenTheFloorIsNarrowed()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var court = await ThreeWayCourtAsync(context, sut, "Roster Narrowing Courts");

        // Act: three pickleball courts become two.
        await sut.UpdateDivisionsAsync(
            court.CourtId,
            new UpdateCourtDivisionsRequest(
                [new CourtSportInput(court.Pickleball, 2)],
                "Re-marked narrower"),
            Admin(),
            CancellationToken.None);

        // Assert: part three stops being sold and stays on the record. A
        // booking taken against it still has to resolve to something, and a
        // receipt for a court that no longer exists is still a receipt.
        var parts = await PartsAsync(context, court.PickleballLinkId);

        using (new AssertionScope())
        {
            parts.Should().HaveCount(3);
            parts.Single(unit => unit.DivisionNumber == 3).IsActive.Should().BeFalse();
            parts.Count(unit => unit.IsActive).Should().Be(2);
        }
    }

    [Fact]
    public async Task Roster_ShouldBringBackTheSamePartWhenTheFloorIsMarkedOutAgain()
    {
        // Arrange: narrowed to two, a month before somebody changes their mind.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var court = await ThreeWayCourtAsync(context, sut, "Roster Restored Courts");
        var third = (await PartsAsync(context, court.PickleballLinkId))
            .Single(unit => unit.DivisionNumber == 3);

        await sut.UpdateDivisionsAsync(
            court.CourtId,
            new UpdateCourtDivisionsRequest([new CourtSportInput(court.Pickleball, 2)], "Narrowed"),
            Admin(),
            CancellationToken.None);

        // Act
        await sut.UpdateDivisionsAsync(
            court.CourtId,
            new UpdateCourtDivisionsRequest([new CourtSportInput(court.Pickleball, 3)], "Widened again"),
            Admin(),
            CancellationToken.None);

        // Assert: the same row, not a replacement wearing its number. This is
        // the reason these are stored rather than counted out on the way past.
        var restored = (await PartsAsync(context, court.PickleballLinkId))
            .Single(unit => unit.DivisionNumber == 3);

        using (new AssertionScope())
        {
            restored.Id.Should().Be(third.Id);
            restored.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Roster_ShouldCallAWholeCourtDividedOnceTheFloorIsMarkedOut()
    {
        // Arrange: volleyball is played across the whole floor.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var court = await ThreeWayCourtAsync(context, sut, "Roster Redescribed Courts");
        var whole = (await PartsAsync(context, court.VolleyballLinkId)).Single();

        // Act
        await sut.UpdateDivisionsAsync(
            court.CourtId,
            new UpdateCourtDivisionsRequest(
                [new CourtSportInput(court.Volleyball, 2)],
                "Two courts across"),
            Admin(),
            CancellationToken.None);

        // Assert: part one of two is the row that used to be the whole court.
        // Nothing else on it could say which -- both carry number one.
        var updated = (await PartsAsync(context, court.VolleyballLinkId))
            .Single(unit => unit.DivisionNumber == 1);

        using (new AssertionScope())
        {
            updated.Id.Should().Be(whole.Id);
            updated.Kind.Should().Be(BookableCourtKind.Divided);
        }
    }

    [Fact]
    public async Task Roster_ShouldAgreeWithTheDivisionCountOnEverySportInTheDatabase()
    {
        // Arrange
        await using var context = database.CreateContext();

        // Act: whatever wrote these rows -- the roster on a court edit, or the
        // migration's backfill over the courts that were already here -- the
        // answer has to be the same one. Asserting the shared invariant rather
        // than one implementation is what lets the backfill be checked at all
        // without copying its SQL into the test and proving only that the copy
        // agrees with itself.
        var disagreements = await context.CourtSports
            .AsNoTracking()
            .Select(link => new
            {
                link.Id,
                link.Divisions,
                Parts = link.BookableCourts.Count(unit => unit.IsActive),
                Highest = link.BookableCourts
                    .Where(unit => unit.IsActive)
                    .Max(unit => (int?)unit.DivisionNumber)
            })
            .Where(row => row.Parts != row.Divisions || row.Highest != row.Divisions)
            .ToListAsync();

        // Assert: one part per division, numbered from one without gaps. A gap
        // is a court a customer is offered and the booking engine cannot find.
        disagreements.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_ShouldTellTheConsoleWhatTheCourtActuallySells()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var court = await ThreeWayCourtAsync(context, sut, "Roster Console Courts");

        // Act
        var listed = await sut.ListAsync(
            await context.Courts
                .Where(candidate => candidate.Id == court.CourtId)
                .Select(candidate => candidate.FacilityId)
                .SingleAsync(),
            CancellationToken.None);

        // Assert: the same five names the public listing shows, named by the
        // same rule. An admin reading a different count from the customer is
        // an admin who cannot answer the phone call about it.
        var units = listed.Single(candidate => candidate.Id == court.CourtId).BookableCourts;

        using (new AssertionScope())
        {
            units.Select(unit => unit.Name).Should().Equal(
                "Che court 1",
                "Che court 1 \u00b7 Pickleball 1",
                "Che court 1 \u00b7 Pickleball 2",
                "Che court 1 \u00b7 Pickleball 3",
                "Che court 1");
            units.Select(unit => unit.Kind).Should().Equal(
                BookableCourtKind.Whole,
                BookableCourtKind.Divided,
                BookableCourtKind.Divided,
                BookableCourtKind.Divided,
                BookableCourtKind.Whole);
        }
    }

    /// <summary>
    /// One floor sold three ways, which is the shape every rule here exists
    /// for: basketball and volleyball played whole, pickleball three across.
    /// </summary>
    private static async Task<ThreeWayCourt> ThreeWayCourtAsync(
        AppDbContext context,
        CourtService sut,
        string facilityName)
    {
        var basketball = await SportIdAsync(context, "basketball");
        var volleyball = await SportIdAsync(context, "volleyball");
        var pickleball = await SportIdAsync(context, "pickleball");
        var owner = await AddOwnerAsync(context);

        var created = await sut.CreateAsync(
            Request(
                owner,
                newFacility: NewFacility(facilityName),
                sportIds: [basketball, volleyball, pickleball]) with
            {
                Court = Court([basketball, volleyball, pickleball]) with
                {
                    Name = "Che court 1",
                    Sports =
                    [
                        new CourtSportInput(basketball, 1),
                        new CourtSportInput(volleyball, 1),
                        new CourtSportInput(pickleball, 3)
                    ]
                }
            },
            Admin(),
            CancellationToken.None);

        var links = await context.CourtSports
            .AsNoTracking()
            .Where(link => link.CourtId == created.Value!.CourtId)
            .ToDictionaryAsync(link => link.SportId, link => link.Id);

        return new ThreeWayCourt(
            created.Value!.CourtId,
            volleyball,
            pickleball,
            links[volleyball],
            links[pickleball]);
    }

    private sealed record ThreeWayCourt(
        Guid CourtId,
        Guid Volleyball,
        Guid Pickleball,
        Guid VolleyballLinkId,
        Guid PickleballLinkId);

    private static async Task<BookableCourt[]> UnitsAsync(AppDbContext context, Guid courtId) =>
        await context.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.CourtId == courtId)
            .OrderBy(unit => unit.DivisionNumber)
            .ToArrayAsync();

    private static async Task<BookableCourt[]> PartsAsync(AppDbContext context, Guid courtSportId) =>
        await context.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.CourtSportId == courtSportId)
            .OrderBy(unit => unit.DivisionNumber)
            .ToArrayAsync();

    private static ActivityCatalog CreateCatalog(AppDbContext context) =>
        new(
            context,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheSignal(),
            new FixedTimeProvider(Now),
            NullLogger<ActivityCatalog>.Instance);

    /// <summary>
    /// A term covering today. The public catalogue only offers owners who are
    /// live, so a court test that wants to appear in it needs one.
    /// </summary>
    private static async Task AddLiveContractAsync(AppDbContext context, Guid ownerId)
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        context.FacilityOwnerContracts.Add(new FacilityOwnerContract(
            ownerId,
            today.AddMonths(-1),
            today.AddMonths(11),
            Guid.NewGuid(),
            null,
            Now));
        await context.SaveChangesAsync();
    }

    private static AuditActor Admin() => new(Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private static CreateCourtRequest Request(
        Guid ownerId,
        IReadOnlyCollection<Guid> sportIds,
        Guid? facilityId = null,
        NewFacilityInput? newFacility = null) =>
        new(ownerId, facilityId, newFacility, Court(sportIds));

    private static CourtInput Court(
        IReadOnlyCollection<Guid> sportIds,
        int divisions = 1) => new(
        "Court 1",
        10,
        "The near court.",
        [.. sportIds.Select(id => new CourtSportInput(id, divisions))],
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
