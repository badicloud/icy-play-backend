using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

/// <summary>
/// The utilization report, and mostly one question: what happens on a floor
/// that is marked out three ways.
///
/// The bookings are made straight against the domain rather than through
/// <see cref="BookingService"/>. A report test wants to say "these exact hours
/// were sold" and then check the arithmetic; going the long way round would
/// make what is being counted a consequence of a dozen other rules.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UtilizationTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Monday = new(2026, 9, 14);
    private static readonly DateOnly Tuesday = new(2026, 9, 15);

    /// <summary>The fixture venue opens six to ten, every day: sixteen hours.</summary>
    private const int MinutesOpenPerDay = 16 * 60;

    [Fact]
    public async Task ShouldCountTheHoursTheVenueWasOpenAndSellable()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Open Hours");
        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Tuesday),
            CancellationToken.None);

        using var _ = new AssertionScope();
        result.Succeeded.Should().BeTrue();
        result.Value!.OpenMinutes.Should().Be(MinutesOpenPerDay * 2);
        result.Value.InUseMinutes.Should().Be(0);
        result.Value.Courts.Should().HaveCount(1);
    }

    /// <summary>
    /// The reason this report has two numbers rather than one.
    ///
    /// Two parts of one floor sold for the same hour is one hour of floor and
    /// two hours of court. Counting the second as utilization would let a floor
    /// marked out three ways report three hundred per cent, and counting only
    /// the first would lose what the venue actually sold.
    /// </summary>
    [Fact]
    public async Task ShouldCountAnHourOnceHoweverManyPartsOfTheFloorWereSold()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Side By Side");

        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));
        await ConfirmAsync(context, venue, venue.Pickleball2, Monday, new TimeOnly(9, 0));

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);

        var court = result.Value!.Courts.Single();

        using var _ = new AssertionScope();
        court.InUseMinutes.Should().Be(60, "the floor had somebody on it for one hour");
        court.SoldMinutes.Should().Be(120, "two of its parts were sold for that hour");
        court.OpenMinutes.Should().Be(MinutesOpenPerDay);

        court.Units.Single(unit => unit.BookableCourtId == venue.Pickleball1)
            .SoldMinutes.Should().Be(60);
        court.Units.Single(unit => unit.BookableCourtId == venue.Pickleball2)
            .SoldMinutes.Should().Be(60);
        court.Units.Single(unit => unit.BookableCourtId == venue.Pickleball3)
            .SoldMinutes.Should().Be(0);
    }

    /// <summary>
    /// A part the venue has stopped marking out still has to show what it sold,
    /// or the rows stop adding up to the court's own total and nothing says
    /// what is missing.
    /// </summary>
    [Fact]
    public async Task ShouldStillListARetiredPartThatSoldHours()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Retired");

        await ConfirmAsync(context, venue, venue.Pickleball3, Monday, new TimeOnly(9, 0));

        await RetireAsync(context, venue.Pickleball3);

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);

        var court = result.Value!.Courts.Single();
        var gone = court.Units.SingleOrDefault(unit => unit.BookableCourtId == venue.Pickleball3);

        using var _ = new AssertionScope();
        gone.Should().NotBeNull("it sold an hour before it was retired");
        gone!.IsRetired.Should().BeTrue();
        gone.SoldMinutes.Should().Be(60);

        court.Units.Sum(unit => unit.SoldMinutes).Should().Be(
            court.SoldMinutes,
            "the rows have to add up to the figure above them");
    }

    [Fact]
    public async Task ShouldLeaveOutARetiredPartThatSoldNothing()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Retired Quiet");

        await RetireAsync(context, venue.Pickleball3);

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);

        result.Value!.Courts.Single().Units
            .Should().NotContain(unit => unit.BookableCourtId == venue.Pickleball3);
    }

    [Fact]
    public async Task ShouldNotCountADayTheCourtWasClosedForWork()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Maintenance");

        // The whole of the Monday, on the court itself.
        context.MaintenancePeriods.Add(new MaintenancePeriod(
            venue.FacilityId,
            venue.CourtId,
            new DateTimeOffset(Monday.ToDateTime(new TimeOnly(0, 0)), TimeSpan.Zero),
            new DateTimeOffset(Monday.ToDateTime(new TimeOnly(23, 59)), TimeSpan.Zero),
            "Resurfacing",
            venue.OwnerUserId,
            Now));
        await context.SaveChangesAsync();

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Tuesday),
            CancellationToken.None);

        var court = result.Value!.Courts.Single();

        using var _ = new AssertionScope();
        court.OpenMinutes.Should().Be(
            MinutesOpenPerDay,
            "a court shut for work was never on sale, and counting it would read as unsold");

        // Shown rather than swallowed: a venue looking at a bad month is owed
        // the reason, and "we were resurfacing" is the reason.
        court.MaintenanceMinutes.Should().Be(MinutesOpenPerDay);
        court.MaintenanceDays.Should().Be(1);
        court.OpenDays.Should().Be(1);
    }

    [Fact]
    public async Task ShouldCountSoldHoursAgainstTheDaysTheCourtWasOpen()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Sold");

        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));
        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(10, 0));

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);

        var court = result.Value!.Courts.Single();

        using var _ = new AssertionScope();
        court.InUseMinutes.Should().Be(120);
        court.OpenMinutes.Should().Be(MinutesOpenPerDay);
        court.OpenDays.Should().Be(1);
        court.MaintenanceMinutes.Should().Be(0);
    }

    /// <summary>
    /// A venue open thirteen hours on ninety-minute slots offers eight of them
    /// and keeps the last half hour. Counting that half hour as open would put
    /// a ceiling on utilization that no court could reach.
    /// </summary>
    [Fact]
    public async Task ShouldRoundOpenMinutesDownToWholeSlots()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Slot Rounding", slotLengthMinutes: 90);
        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);

        // Sixteen hours is 960 minutes; ten whole slots of ninety is 900.
        result.Value!.OpenMinutes.Should().Be(900);
    }

    [Fact]
    public async Task ShouldCountWhatIsWaitingOnTheDeskApartFromWhatWasPlayed()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Waiting");

        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));
        await WaitingAsync(context, venue, venue.Basketball, Monday, new TimeOnly(11, 0));

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);

        var court = result.Value!.Courts.Single();

        using var _ = new AssertionScope();
        court.InUseMinutes.Should().Be(60, "only the confirmed hour was played");
        court.AwaitingMinutes.Should().Be(60, "the other is neither used nor lost");
    }

    /// <summary>
    /// Left out of the response rather than hidden by the page: a figure the
    /// screen does not draw is still a figure anybody can read off the network
    /// tab.
    /// </summary>
    [Fact]
    public async Task ShouldLeaveTheMoneyOutForAnAttendant()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Attendant");

        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));

        var attendant = new User($"attendant-{Guid.NewGuid():N}@example.com", "Desk Hand", null);
        attendant.SetPasswordHash("hash");
        context.Users.Add(attendant);
        context.UserRoles.Add(new UserRole(attendant.Id, UserRoleName.FacilityAttendant));
        context.FacilityAttendants.Add(new FacilityAttendant(venue.FacilityId, attendant.Id, Now));
        await context.SaveChangesAsync();

        var sut = CreateService(context);
        var query = new UtilizationQuery(Monday, Monday);

        var theirs = await sut.UtilizationAsync(attendant.Id, query, CancellationToken.None);
        var owners = await sut.UtilizationAsync(venue.OwnerUserId, query, CancellationToken.None);

        using var _ = new AssertionScope();
        theirs.Value!.Rental.Should().BeNull();
        theirs.Value.Courts.Single().Rental.Should().BeNull();
        theirs.Value.Courts.Single().Units.Should().OnlyContain(unit => unit.Rental == null);
        theirs.Value.Courts.Single().InUseMinutes.Should().Be(
            60,
            "the hours are the desk's own business even when the money is not");

        owners.Value!.Rental.Should().NotBeNull();
        owners.Value.Courts.Single().Rental.Should().Be(500m);
    }

    [Fact]
    public async Task ShouldRefuseAVenueThePersonDoesNotWork()
    {
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "Utilization Mine");
        var theirs = await VenueAsync(context, "Utilization Theirs");
        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            mine.OwnerUserId,
            new UtilizationQuery(Monday, Monday, theirs.FacilityId),
            CancellationToken.None);

        using var _ = new AssertionScope();
        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(DeskFailure.NotAttended);
    }

    [Fact]
    public async Task ShouldRefuseARangeThatEndsBeforeItStarts()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Backwards");
        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Tuesday, Monday),
            CancellationToken.None);

        result.Failure.Should().Be(DeskFailure.WindowBackwards);
    }

    [Fact]
    public async Task ShouldRefuseAStretchLongerThanAYear()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Too Wide");
        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday.AddDays(UtilizationQueryValidator.MostDays)),
            CancellationToken.None);

        result.Failure.Should().Be(DeskFailure.ReportWindowTooWide);
    }

    /// <summary>
    /// The three states have to come to the whole, or the widgets on the desk
    /// disagree with each other in front of somebody standing at it.
    /// </summary>
    [Fact]
    public async Task SnapshotShouldSortEveryBookableCourtIntoExactlyOneState()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Snapshot Sorting");

        // Nine in the morning at the venue is one in the morning UTC, so the
        // fixture's own Now is inside this hour on the venue's clock.
        var at = new DateTimeOffset(2026, 9, 14, 1, 30, 0, TimeSpan.Zero);

        await SeedAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0), confirmed: true);

        var sut = CreateService(context, at);

        var result = await sut.SnapshotAsync(venue.OwnerUserId, venue.FacilityId, CancellationToken.None);
        var now = result.Value!;

        using var _ = new AssertionScope();
        now.Courts.Should().Be(1);
        now.BookableCourts.Should().Be(4, "basketball whole, and pickleball three across");
        now.BookedNow.Should().Be(1);
        now.UnderMaintenanceNow.Should().Be(0);
        now.AvailableNow.Should().Be(3);

        (now.AvailableNow + now.BookedNow + now.UnderMaintenanceNow)
            .Should().Be(now.BookableCourts, "the three states are the whole");
    }

    /// <summary>
    /// A closure on the floor takes every part of it, and takes them out of
    /// free rather than leaving them there to be sold.
    /// </summary>
    [Fact]
    public async Task SnapshotShouldCountAClosedFloorAsMaintenanceRatherThanFree()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Snapshot Maintenance");

        var at = new DateTimeOffset(2026, 9, 14, 1, 30, 0, TimeSpan.Zero);

        context.MaintenancePeriods.Add(new MaintenancePeriod(
            venue.FacilityId,
            venue.CourtId,
            at.AddHours(-1),
            at.AddHours(1),
            "Resurfacing",
            venue.OwnerUserId,
            at));
        await context.SaveChangesAsync();

        var sut = CreateService(context, at);

        var result = await sut.SnapshotAsync(venue.OwnerUserId, venue.FacilityId, CancellationToken.None);
        var now = result.Value!;

        using var _ = new AssertionScope();
        now.UnderMaintenanceNow.Should().Be(4);
        now.AvailableNow.Should().Be(0, "a court shut for work is not free to sell");
        now.BookedNow.Should().Be(0);
    }

    /// <summary>
    /// Re-marks the floor from three pickleball parts to two, the way the court
    /// service does it: the part is retired AND the division count comes down
    /// with it.
    ///
    /// Retiring one on its own leaves the pair claiming three parts and owning
    /// two — a state the app never creates, and one CourtTests polices across
    /// the whole database. A test that left it behind would fail a different
    /// test in a different file, which is a hard morning for whoever finds it.
    /// </summary>
    private static async Task RetireAsync(AppDbContext context, Guid bookableCourtId)
    {
        var unit = await context.BookableCourts
            .Include(candidate => candidate.CourtSport)
                .ThenInclude(pair => pair.BookableCourts)
            .SingleAsync(candidate => candidate.Id == bookableCourtId);

        unit.Retire(Now);
        unit.CourtSport.SetDivisions(
            unit.CourtSport.BookableCourts.Count(part => part.IsActive),
            Now);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// The line and the total have to be the same sum.
    ///
    /// This is the reason the over-time read folds the same walk rather than
    /// counting the days again. A venue reading a chart beside a percentage is
    /// exactly who would find them disagreeing.
    /// </summary>
    [Fact]
    public async Task OverTimeShouldAddUpToTheUtilizationTotals()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hours Over Time Totals");

        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));
        await ConfirmAsync(context, venue, venue.Pickleball1, Tuesday, new TimeOnly(10, 0));
        await ConfirmAsync(context, venue, venue.Pickleball2, Tuesday, new TimeOnly(10, 0));

        var sut = CreateService(context);

        var totals = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Tuesday, venue.FacilityId),
            CancellationToken.None);

        var line = await sut.HoursOverTimeAsync(
            venue.OwnerUserId,
            new HoursQuery(Monday, Tuesday, HoursGrain.Day, venue.FacilityId),
            CancellationToken.None);

        var court = totals.Value!.Courts.Single();
        var rows = line.Value!.Rows;

        using var _ = new AssertionScope();
        rows.Sum(row => row.OpenMinutes).Should().Be(court.OpenMinutes);
        rows.Sum(row => row.SoldMinutes).Should().Be(court.InUseMinutes);
        rows.Sum(row => row.MaintenanceMinutes).Should().Be(court.MaintenanceMinutes);

        // Two parts sold for the same hour is one hour of floor, on the line
        // as well as in the total.
        rows.Single(row => row.Starts == Tuesday).SoldMinutes.Should().Be(60);
    }

    /// <summary>
    /// A day the venue could not have traded on is absent, not zero. A chart
    /// draws a gap where there was no offer, rather than a floor where nobody
    /// bought.
    /// </summary>
    [Fact]
    public async Task OverTimeShouldLeaveOutADayTheCourtCouldNotHaveTraded()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hours Over Time Gap", shutOn: Monday.DayOfWeek);
        var sut = CreateService(context);

        var line = await sut.HoursOverTimeAsync(
            venue.OwnerUserId,
            new HoursQuery(Monday, Tuesday, HoursGrain.Day, venue.FacilityId),
            CancellationToken.None);

        using var _ = new AssertionScope();
        line.Value!.Rows.Should().NotContain(row => row.Starts == Monday);
        line.Value.Rows.Should().Contain(row => row.Starts == Tuesday);

        // The period is still there, or the chart could not know a gap belongs
        // on the Monday rather than the line simply starting on the Tuesday.
        line.Value.Periods.Select(period => period.Starts)
            .Should().Equal(Monday, Tuesday);
    }

    [Fact]
    public async Task OverTimeShouldClampTheFirstAndLastWeekToTheRangeAskedFor()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hours Over Time Periods");
        var sut = CreateService(context);

        // A Wednesday to the following Tuesday: two weeks, each partly outside.
        var wednesday = new DateOnly(2026, 9, 16);
        var nextTuesday = new DateOnly(2026, 9, 22);

        var line = await sut.HoursOverTimeAsync(
            venue.OwnerUserId,
            new HoursQuery(wednesday, nextTuesday, HoursGrain.Week, venue.FacilityId),
            CancellationToken.None);

        line.Value!.Periods.Should().Equal(
            new ReportPeriod(wednesday, new DateOnly(2026, 9, 20)),
            new ReportPeriod(new DateOnly(2026, 9, 21), nextTuesday));
    }

    [Fact]
    public async Task OverTimeShouldGatherDaysIntoTheWeekTheyFallIn()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hours Over Time Weekly");

        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));
        await ConfirmAsync(context, venue, venue.Basketball, Tuesday, new TimeOnly(9, 0));

        var sut = CreateService(context);

        var line = await sut.HoursOverTimeAsync(
            venue.OwnerUserId,
            new HoursQuery(Monday, Tuesday, HoursGrain.Week, venue.FacilityId),
            CancellationToken.None);

        var week = line.Value!.Rows.Single();

        using var _ = new AssertionScope();
        week.SoldMinutes.Should().Be(120, "both days fall in one week");
        // Clamped to what was asked for, not to the Sunday the week really ends on.
        week.Starts.Should().Be(Monday);
        week.Ends.Should().Be(Tuesday);
    }

    [Fact]
    public async Task OverTimeShouldRefuseAGrainNobodyCanAskFor()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hours Over Time Grain");
        var sut = CreateService(context);

        var line = await sut.HoursOverTimeAsync(
            venue.OwnerUserId,
            new HoursQuery(Monday, Tuesday, "Fortnight", venue.FacilityId),
            CancellationToken.None);

        line.Failure.Should().Be(DeskFailure.UnknownGrain);
    }

    private static Task ConfirmAsync(
        AppDbContext context,
        Venue venue,
        Guid bookableCourtId,
        DateOnly date,
        TimeOnly startsAt) =>
        SeedAsync(context, venue, bookableCourtId, date, startsAt, confirmed: true);

    private static Task WaitingAsync(
        AppDbContext context,
        Venue venue,
        Guid bookableCourtId,
        DateOnly date,
        TimeOnly startsAt) =>
        SeedAsync(context, venue, bookableCourtId, date, startsAt, confirmed: false);

    private static async Task SeedAsync(
        AppDbContext context,
        Venue venue,
        Guid bookableCourtId,
        DateOnly date,
        TimeOnly startsAt,
        bool confirmed)
    {
        var booking = new Booking(
            bookableCourtId,
            venue.CustomerUserId,
            BookingKind.Hourly,
            "Desk court 1",
            "Utilization",
            "Pickleball",
            15m,
            date,
            date,
            30,
            Now);

        booking.AddSlot(new BookingSlot(
            booking.Id,
            venue.CourtId,
            bookableCourtId,
            date,
            startsAt,
            startsAt.AddHours(1),
            CourtRateKind.Standard,
            500m,
            15m,
            Now));

        booking.SubmitForVerification(Now);

        if (confirmed)
        {
            booking.Confirm(Now);
        }

        context.Bookings.Add(booking);
        await context.SaveChangesAsync();
    }

    private static async Task<Venue> VenueAsync(
        AppDbContext context,
        string facilityName,
        int slotLengthMinutes = 60,
        DayOfWeek? shutOn = null)
    {
        var courts = CreateCourtService(context);

        var basketball = await SportIdAsync(context, "basketball");
        var pickleball = await SportIdAsync(context, "pickleball");

        var ownerUser = new User($"util-owner-{Guid.NewGuid():N}@example.com", "Report Owner", null);
        ownerUser.SetPasswordHash("hash");
        context.Users.Add(ownerUser);
        context.UserRoles.Add(new UserRole(ownerUser.Id, UserRoleName.FacilityOwner));

        var customer = new User($"util-booker-{Guid.NewGuid():N}@example.com", "Report Rita", null);
        customer.SetPasswordHash("hash");
        context.Users.Add(customer);
        context.UserRoles.Add(new UserRole(customer.Id, UserRoleName.Customer));

        var owner = new FacilityOwner(ownerUser.Id, "Report Ventures", "billing@example.com", null);
        context.FacilityOwners.Add(owner);

        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        context.FacilityOwnerContracts.Add(new FacilityOwnerContract(
            owner.Id,
            today.AddMonths(-1),
            today.AddMonths(11),
            Guid.NewGuid(),
            null,
            Now));
        await context.SaveChangesAsync();

        var created = await courts.CreateAsync(
            new CreateCourtRequest(
                owner.Id,
                null,
                NewFacility(facilityName, shutOn),
                new CourtInput(
                    "Desk court 1",
                    10,
                    "The near court.",
                    [new CourtSportInput(basketball, 1), new CourtSportInput(pickleball, 3)],
                    basketball,
                    CourtVenueType.Covered,
                    CourtSurface.Concrete,
                    true,
                    "Full court",
                    12,
                    null,
                    slotLengthMinutes,
                    slotLengthMinutes,
                    0,
                    true,
                    [],
                    [])),
            Admin(),
            CancellationToken.None);

        await courts.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [
                    new SportPricingInput(basketball, 500m, 600m, 550m, 700m),
                    new SportPricingInput(pickleball, 500m, 600m, 550m, 700m)
                ],
                new PeakWindowInput(new TimeOnly(17, 0), new TimeOnly(20, 0), true, true),
                "Opening rates"),
            Admin(),
            CancellationToken.None);

        var units = await context.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.CourtId == created.Value.CourtId)
            .Select(unit => new { unit.Id, unit.CourtSport.SportId, unit.DivisionNumber })
            .ToListAsync();

        Guid Unit(Guid sportId, int number) => units
            .Single(unit => unit.SportId == sportId && unit.DivisionNumber == number).Id;

        var facilityId = await context.Courts
            .AsNoTracking()
            .Where(court => court.Id == created.Value.CourtId)
            .Select(court => court.FacilityId)
            .SingleAsync();

        return new Venue(
            facilityId,
            created.Value.CourtId,
            Unit(basketball, 1),
            Unit(pickleball, 1),
            Unit(pickleball, 2),
            Unit(pickleball, 3),
            ownerUser.Id,
            customer.Id);
    }

    private sealed record Venue(
        Guid FacilityId,
        Guid CourtId,
        Guid Basketball,
        Guid Pickleball1,
        Guid Pickleball2,
        Guid Pickleball3,
        Guid OwnerUserId,
        Guid CustomerUserId);

    private static async Task<Guid> SportIdAsync(AppDbContext context, string key) =>
        await context.Sports.Where(sport => sport.Key == key).Select(sport => sport.Id).SingleAsync();

    private static DeskService CreateService(AppDbContext context, DateTimeOffset? at = null) => new(
        context,
        new SilentNotifier(),
        new AuditLogger(context, new FixedTimeProvider(at ?? Now)),
        new FixedTimeProvider(at ?? Now),
        NullLogger<DeskService>.Instance);

    private static CourtService CreateCourtService(AppDbContext context) => new(
        context,
        new ActivityCatalog(
            context,
            new MemoryCache(new MemoryCacheOptions()),
            new CatalogCacheSignal(),
            new FixedTimeProvider(Now),
            NullLogger<ActivityCatalog>.Instance),
        new AuditLogger(context, new FixedTimeProvider(Now)),
        Assets(),
        new FixedTimeProvider(Now),
        NullLogger<CourtService>.Instance);

    private static CloudinaryAssetService Assets() => new(
        Options.Create(new CloudinaryOptions
        {
            CloudName = "icyplay-test",
            ApiKey = "123456789012345",
            ApiSecret = "test-api-secret"
        }),
        new FixedTimeProvider(Now));

    private static AuditActor Admin() => new(Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private static NewFacilityInput NewFacility(string name, DayOfWeek? shutOn = null) => new(
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
            // A day with no hours is a day the venue is shut: that is how a
            // closure is said here, and what the availability reads back.
            .. Enum.GetValues<DayOfWeek>()
                .Where(day => day != shutOn)
                .Select(day => new OperatingHourInput(
                    day,
                    new TimeOnly(6, 0),
                    new TimeOnly(22, 0)))
        ],
        []);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SilentNotifier : IBookingNotifier
    {
        public Task PaymentSubmittedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;

        public Task BookingConfirmedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;

        public Task UpgradeSubmittedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpgradeApprovedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) =>
            Task.CompletedTask;

        public Task BookingMovedAsync(Booking booking, BookingMoveNotice notice, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
