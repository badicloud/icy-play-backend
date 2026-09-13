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
