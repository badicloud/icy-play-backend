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

    /// <summary>
    /// A court that sold nothing in the range still says when it last did, even
    /// when that was before the range began. That is what tells a quiet month
    /// from a court nobody wants.
    /// </summary>
    [Fact]
    public async Task ShouldSayWhenACourtLastSoldEvenBeforeTheRange()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Last Sold");

        // Sold on the Monday; the report is asked about the Tuesday alone.
        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Tuesday, Tuesday),
            CancellationToken.None);

        var court = result.Value!.Courts.Single();

        using var _ = new AssertionScope();
        court.InUseMinutes.Should().Be(0, "nothing sold on the Tuesday");
        court.LastSoldOn.Should().Be(Monday);
        court.Units.Single(unit => unit.BookableCourtId == venue.Pickleball1)
            .LastSoldOn.Should().Be(Monday);
        court.Units.Single(unit => unit.BookableCourtId == venue.Basketball)
            .LastSoldOn.Should().BeNull("it has never been sold");
    }

    /// <summary>
    /// A hold that was never confirmed is not a sale, so it does not move the
    /// last-sold date either.
    /// </summary>
    [Fact]
    public async Task ShouldNotCountAnUnconfirmedBookingAsTheLastSale()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Utilization Last Sold Waiting");

        await WaitingAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));

        var sut = CreateService(context);

        var result = await sut.UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Tuesday),
            CancellationToken.None);

        result.Value!.Courts.Single().LastSoldOn.Should().BeNull();
    }

    /// <summary>
    /// How many parts sold in each period, for the not-sold line. A part sold
    /// twice is one part sold, and two parts sold the same hour are two.
    /// </summary>
    [Fact]
    public async Task OverTimeShouldCountThePartsThatSoldInEachPeriod()
    {
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hours Over Time Parts");

        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(9, 0));
        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(11, 0));
        await ConfirmAsync(context, venue, venue.Pickleball1, Tuesday, new TimeOnly(10, 0));
        await ConfirmAsync(context, venue, venue.Pickleball2, Tuesday, new TimeOnly(10, 0));

        var sut = CreateService(context);

        var line = await sut.HoursOverTimeAsync(
            venue.OwnerUserId,
            new HoursQuery(Monday, Tuesday, HoursGrain.Day, venue.FacilityId),
            CancellationToken.None);

        var monday = line.Value!.Rows.Single(row => row.Starts == Monday);
        var tuesday = line.Value.Rows.Single(row => row.Starts == Tuesday);

        using var _ = new AssertionScope();
        // Basketball whole, and pickleball three across.
        monday.Parts.Should().Be(4);
        monday.PartsSold.Should().Be(1, "basketball twice is still one part");
        tuesday.PartsSold.Should().Be(2, "two pickleball parts, the same hour");
    }

    /// <summary>
    /// Missed income counts only hours that have begun, prices the court at its
    /// main sport, and prices each sport court on its own — leaving out every
    /// hour a clashing game had the floor, because those were not for sale.
    ///
    /// Monday at 09:00 UTC is 5pm in Manila: the 6am to 5pm hours have begun,
    /// twelve of them, and 5pm is peak. Pickleball 1 is sold at nine and the
    /// whole floor for basketball at ten.
    /// </summary>
    [Fact]
    public async Task MissedShouldPriceWhatCouldHaveBeenSoldAndHasBegun()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Missed Income Courts");

        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));
        await ConfirmAsync(context, venue, venue.Basketball, Monday, new TimeOnly(10, 0));

        var sut = CreateService(context);

        // Act: Tuesday is in the range and still ahead, so none of it counts.
        var result = await sut.MissedAsync(
            venue.OwnerUserId,
            new HoursQuery(Monday, Tuesday, HoursGrain.Day),
            CancellationToken.None);

        // Assert
        var court = result.Value!.Courts.Single();
        UnitMissed Part(Guid id) => court.Units.Single(unit => unit.BookableCourtId == id);

        using var _ = new AssertionScope();
        result.Succeeded.Should().BeTrue();

        court.MainSportName.Should().Be("Basketball");
        court.OpenMinutes.Should().Be(12 * 60, "the hours from six to five have begun");
        court.NotSoldMinutes.Should().Be(10 * 60, "the floor had somebody on it at nine and at ten");
        court.PeakNotSoldMinutes.Should().Be(60);

        // Basketball, the main sport: blocked at nine by pickleball and at ten
        // by itself. Nine standard hours and the peak one.
        court.Missed.Should().Be((9 * 500m) + 600m);
        court.PeakMissed.Should().Be(600m);
        Part(venue.Basketball).Missed.Should().Be(court.Missed);

        // Pickleball 2 sits beside pickleball 1, so nine o'clock was still for
        // sale; ten was not, the basketball had the floor.
        Part(venue.Pickleball2).NotSoldMinutes.Should().Be(11 * 60);
        Part(venue.Pickleball2).Missed.Should().Be((10 * 500m) + 600m);
        Part(venue.Pickleball1).NotSoldMinutes.Should().Be(10 * 60);

        // Tuesday has not happened yet.
        result.Value.Periods.Should().HaveCount(2);
        result.Value.Periods.Last().OpenMinutes.Should().Be(0);
        result.Value.Periods.First().Missed.Should().Be(court.Missed);
    }

    /// <summary>
    /// Court changes are read back out of the audit trail as a venue would say
    /// them: the court added, its prices set, pickleball re-marked from three
    /// courts to two, and the court closed for work — each with the lines that
    /// changed, the reason given, and who did it.
    /// </summary>
    [Fact]
    public async Task CourtChangesShouldDescribeWhatHappenedToTheCourts()
    {
        // Arrange: the venue helper adds the court and prices it.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Court Changes Courts");
        var courts = CreateCourtService(context);
        var pickleball = await SportIdAsync(context, "pickleball");
        var basketball = await SportIdAsync(context, "basketball");

        await courts.UpdateDivisionsAsync(
            venue.CourtId,
            new UpdateCourtDivisionsRequest(
                [new CourtSportInput(basketball, 1), new CourtSportInput(pickleball, 2)],
                "Wider lanes"),
            Admin(),
            CancellationToken.None);
        await courts.SetCourtMaintenanceAsync(
            venue.CourtId,
            new SetMaintenanceRequest(Now, Now.AddDays(2), "Resurfacing"),
            Admin(),
            CancellationToken.None);
        context.ChangeTracker.Clear();

        // Act
        var result = await CreateService(context).CourtChangesAsync(
            venue.OwnerUserId,
            new CourtChangesQuery(Monday, Monday),
            CancellationToken.None);

        // Assert
        var report = result.Value!;
        var remarked = report.Changes.Single(change => change.Kind == CourtChangeKind.SportsAndDivisions);
        var priced = report.Changes.Single(change => change.Kind == CourtChangeKind.Prices);

        using var _ = new AssertionScope();
        result.Succeeded.Should().BeTrue();
        report.Kinds.Select(kind => kind.Kind).Should().BeEquivalentTo(
        [
            CourtChangeKind.Added,
            CourtChangeKind.SportsAndDivisions,
            CourtChangeKind.Prices,
            CourtChangeKind.Maintenance
        ]);

        remarked.Title.Should().Be("Desk court 1 · Pickleball re-marked");
        remarked.Details.Should().Equal(new ChangeDetail("Pickleball", "3 courts", "2 courts"));
        remarked.Reason.Should().Be("Wider lanes");
        remarked.ActorRole.Should().Be("Platform admin");
        remarked.ActorName.Should().BeNull("an admin's own name is the platform's business");
        remarked.On.Should().Be(Monday);

        priced.Details.Should().Contain(new ChangeDetail("Basketball standard", "Not priced", "₱500"));

        report.Changes.Should().ContainSingle(change => change.Title == "Desk court 1 closed for maintenance")
            .Which.Reason.Should().Be("Resurfacing");

        report.Summary.Courts.Should().Be(1);
        report.Summary.CourtsAdded.Should().Be(1);
        report.Summary.BookableCourts.Should().Be(3, "basketball and two pickleball courts are left");
        report.Summary.BookableCourtsRetired.Should().Be(1);
        report.Summary.PriceChanges.Should().Be(1);
        report.Summary.Closures.Should().Be(1);
        report.Summary.ClosedNow.Should().Be(1);
    }

    /// <summary>
    /// The court mix counts what the venue has now — by venue type and by what
    /// each court is set up for — with each venue type's share of its open
    /// hours sold, the same figures Court utilisation shows. A retired court is
    /// never counted, and listed only when asked for.
    /// </summary>
    [Fact]
    public async Task CourtMixShouldCountWhatTheVenueHasAndListRetiredOnlyWhenAsked()
    {
        // Arrange: one covered court, lit, basketball as its main sport and
        // pickleball three ways, sold for an hour on Monday.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Court Mix Courts");
        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));
        var sut = CreateService(context);

        // Act
        var mix = (await sut.CourtMixAsync(
            venue.OwnerUserId,
            new CourtMixQuery(Monday, Monday),
            CancellationToken.None)).Value!;

        await context.Courts
            .Where(court => court.Id == venue.CourtId)
            .ExecuteUpdateAsync(set => set.SetProperty(court => court.IsActive, false));

        var withoutRetired = (await sut.CourtMixAsync(
            venue.OwnerUserId,
            new CourtMixQuery(Monday, Monday),
            CancellationToken.None)).Value!;
        var withRetired = (await sut.CourtMixAsync(
            venue.OwnerUserId,
            new CourtMixQuery(Monday, Monday, IncludeRetired: true),
            CancellationToken.None)).Value!;

        // Assert
        using var _ = new AssertionScope();
        mix.Summary.Should().Be(new CourtMixSummary(
            Courts: 1,
            BookableCourts: 4,
            UnderRoof: 1,
            WithLighting: 1,
            TakeEvents: 0,
            EventKinds: 0,
            Retired: 0));

        var covered = mix.VenueTypes.Should().ContainSingle().Subject;
        covered.VenueType.Should().Be(CourtVenueType.Covered);
        covered.OpenMinutes.Should().Be(MinutesOpenPerDay);
        covered.InUseMinutes.Should().Be(60);

        mix.Activities.Select(activity => (activity.Name, activity.Courts, activity.BookableCourts, activity.MainOn))
            .Should().Equal(("Basketball", 1, 1, 1), ("Pickleball", 1, 3, 0));

        var row = mix.Courts.Should().ContainSingle().Subject;
        row.Activities.First().Should().Be(new CourtActivity("Basketball", ActivityKind.Sport, true, 1));
        row.IsRetired.Should().BeFalse();

        // Retired: gone from the counts, and from the list unless asked for.
        withoutRetired.Summary.Courts.Should().Be(0);
        withoutRetired.Summary.Retired.Should().Be(1);
        withoutRetired.Courts.Should().BeEmpty();
        withRetired.Courts.Should().ContainSingle().Which.IsRetired.Should().BeTrue();
        withRetired.Summary.Courts.Should().Be(0);
    }

    /// <summary>
    /// The admin's snapshot is the desk's own, added up across the venues in
    /// scope and then owner by owner — and an owner or venue that does not
    /// exist is refused rather than answered with zeros.
    ///
    /// Monday at 09:00 UTC is 5pm in Manila, so a booking at five is on the
    /// court now.
    /// </summary>
    [Fact]
    public async Task PlatformSnapshotShouldAddUpTheDesksFiguresOwnerByOwner()
    {
        // Arrange: two owners, one with somebody on pickleball 1 right now.
        await using var context = database.CreateContext();
        var busy = await VenueAsync(context, "Platform Busy Courts");
        var quiet = await VenueAsync(context, "Platform Quiet Courts");
        await ConfirmAsync(context, busy, busy.Pickleball1, Monday, new TimeOnly(17, 0));

        async Task<Guid> OwnerOf(Venue venue) =>
            await context.Facilities
                .Where(facility => facility.Id == venue.FacilityId)
                .Select(facility => facility.FacilityOwnerId)
                .SingleAsync();

        var busyOwner = await OwnerOf(busy);
        var quietOwner = await OwnerOf(quiet);
        var sut = new PlatformReportService(context, new FixedTimeProvider(Now));

        // Act
        var everyone = await sut.SnapshotAsync(null, null, CancellationToken.None);
        var one = await sut.SnapshotAsync(busyOwner, null, CancellationToken.None);
        var unknownOwner = await sut.SnapshotAsync(Guid.NewGuid(), null, CancellationToken.None);
        var someoneElsesVenue = await sut.SnapshotAsync(busyOwner, quiet.FacilityId, CancellationToken.None);

        // Assert
        using var _ = new AssertionScope();
        everyone.Value!.PerOwner.Select(owner => owner.FacilityOwnerId)
            .Should().Contain([busyOwner, quietOwner]);
        everyone.Value.Total.Courts.Should().Be(everyone.Value.PerOwner.Sum(owner => owner.Snapshot.Courts));

        var theirs = one.Value!.PerOwner.Should().ContainSingle().Subject;
        one.Value.Owners.Should().Be(1);
        one.Value.Venues.Should().Be(1);
        theirs.Snapshot.Should().Be(new VenueSnapshot(
            Courts: 1,
            BookableCourts: 4,
            AvailableNow: 3,
            BookedNow: 1,
            UnderMaintenanceNow: 0));
        one.Value.Total.Should().Be(theirs.Snapshot, "one owner's total is that owner");

        unknownOwner.Failure.Should().Be(PlatformReportFailure.OwnerNotFound);
        someoneElsesVenue.Failure.Should().Be(PlatformReportFailure.VenueNotFound);
    }

    /// <summary>
    /// The admin's court utilisation for one owner is the owner's own report,
    /// figure for figure — money included, because the admin sees what the
    /// owner sees of their own.
    /// </summary>
    [Fact]
    public async Task PlatformUtilizationShouldMatchTheOwnersOwnReport()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Platform Utilization Courts");
        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));
        var ownerId = await context.Facilities
            .Where(facility => facility.Id == venue.FacilityId)
            .Select(facility => facility.FacilityOwnerId)
            .SingleAsync();
        var sut = new PlatformReportService(context, new FixedTimeProvider(Now));

        // Act
        var admin = await sut.UtilizationAsync(ownerId, null, Monday, Monday, CancellationToken.None);
        var owner = await CreateService(context).UtilizationAsync(
            venue.OwnerUserId,
            new UtilizationQuery(Monday, Monday),
            CancellationToken.None);
        var backwards = await sut.UtilizationAsync(ownerId, null, Tuesday, Monday, CancellationToken.None);

        // Assert
        using var _ = new AssertionScope();
        admin.Succeeded.Should().BeTrue();
        admin.Value!.Should().BeEquivalentTo(owner.Value);
        admin.Value!.Rental.Should().Be(500m);
        admin.Value.InUseMinutes.Should().Be(60);
        backwards.Failure.Should().Be(PlatformReportFailure.WindowBackwards);
    }

    /// <summary>
    /// The admin's hours over time for one owner are the owner's own, period
    /// by period and court by court.
    /// </summary>
    [Fact]
    public async Task PlatformHoursOverTimeShouldMatchTheOwnersOwn()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Platform Hours Courts");
        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));
        var ownerId = await context.Facilities
            .Where(facility => facility.Id == venue.FacilityId)
            .Select(facility => facility.FacilityOwnerId)
            .SingleAsync();
        var sut = new PlatformReportService(context, new FixedTimeProvider(Now));
        var query = new HoursQuery(Monday, Tuesday, HoursGrain.Day);

        // Act
        var admin = await sut.HoursOverTimeAsync(ownerId, null, query, CancellationToken.None);
        var owner = await CreateService(context).HoursOverTimeAsync(venue.OwnerUserId, query, CancellationToken.None);
        var nonsense = await sut.HoursOverTimeAsync(ownerId, null, query with { Grain = "Fortnight" }, CancellationToken.None);

        // Assert
        using var _ = new AssertionScope();
        admin.Succeeded.Should().BeTrue();
        admin.Value.Should().BeEquivalentTo(owner.Value);
        admin.Value!.Rows.Sum(row => row.SoldMinutes).Should().Be(60);
        nonsense.Failure.Should().Be(PlatformReportFailure.UnknownGrain);
    }

    /// <summary>
    /// Every other desk report the admin reads for one owner is that owner's
    /// own, figure for figure: the admin's are the desk's code over the
    /// owner's venues, not a second copy of it.
    /// </summary>
    [Fact]
    public async Task PlatformReportsShouldMatchTheOwnersOwn()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Platform Six Courts");
        var elsewhere = await VenueAsync(context, "Platform Six Elsewhere");
        await ConfirmAsync(context, venue, venue.Pickleball1, Monday, new TimeOnly(9, 0));
        var ownerId = await context.Facilities
            .Where(facility => facility.Id == venue.FacilityId)
            .Select(facility => facility.FacilityOwnerId)
            .SingleAsync();
        var admin = new PlatformReportService(context, new FixedTimeProvider(Now));
        var desk = CreateService(context);
        var range = new HoursQuery(Monday, Tuesday, HoursGrain.Day);
        var ct = CancellationToken.None;

        // Act and assert, report by report.
        using var _ = new AssertionScope();

        (await admin.MovesAsync(ownerId, null, range, ct)).Value
            .Should().BeEquivalentTo((await desk.MovesAsync(venue.OwnerUserId, range, ct)).Value);
        (await admin.DeclinesAsync(ownerId, null, range, ct)).Value
            .Should().BeEquivalentTo((await desk.DeclinesAsync(venue.OwnerUserId, range, ct)).Value);
        (await admin.TakingsAsync(ownerId, null, range, ct)).Value
            .Should().BeEquivalentTo((await desk.TakingsAsync(venue.OwnerUserId, range, ct)).Value);
        (await admin.MissedAsync(ownerId, null, range, ct)).Value
            .Should().BeEquivalentTo((await desk.MissedAsync(venue.OwnerUserId, range, ct)).Value);

        var mix = new CourtMixQuery(Monday, Tuesday);
        (await admin.CourtMixAsync(ownerId, null, mix, ct)).Value
            .Should().BeEquivalentTo((await desk.CourtMixAsync(venue.OwnerUserId, mix, ct)).Value);

        var changes = new CourtChangesQuery(Monday, Tuesday);
        (await admin.CourtChangesAsync(ownerId, null, changes, ct)).Value
            .Should().BeEquivalentTo((await desk.CourtChangesAsync(venue.OwnerUserId, changes, ct)).Value);

        // A court at somebody else's venue, and a range longer than takings go.
        (await admin.CourtChangesAsync(ownerId, null, changes with { CourtId = elsewhere.CourtId }, ct)).Failure
            .Should().Be(PlatformReportFailure.CourtNotFound);
        (await admin.TakingsAsync(ownerId, null, range with { From = Monday.AddYears(-6) }, ct)).Failure
            .Should().Be(PlatformReportFailure.TakingsWindowTooWide);
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

        public Task BookingDeclinedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;

        public Task UpgradeSubmittedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpgradeApprovedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MoveRequestedAsync(BookingUpgradeRequest move, CancellationToken ct) => Task.CompletedTask;

        public Task MoveApprovedAsync(BookingUpgradeRequest move, string fromCourtName, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MoveDeclinedAsync(BookingUpgradeRequest move, CancellationToken ct) => Task.CompletedTask;
    }
}
