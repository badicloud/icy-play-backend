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

[Collection(DatabaseCollection.Name)]
public sealed class BookingTests(SqlServerDatabaseFixture database)
{
    // A Monday. The venue opens 6am to 10pm every day, so a date is 16 hours.
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The venue's today. 09:00 UTC is 5pm in Manila, so most of it has gone.</summary>
    private static readonly DateOnly Today = new(2026, 9, 14);

    private static readonly DateOnly Tuesday = new(2026, 9, 15);
    private static readonly DateOnly Wednesday = new(2026, 9, 16);
    private static readonly DateOnly Saturday = new(2026, 9, 19);
    private static readonly DateOnly Thursday = new(2026, 9, 17);
    private static readonly DateOnly NextSaturday = new(2026, 9, 26);
    private static readonly DateOnly Friday = new(2026, 9, 18);
    /// <summary>The day the Sunday-closed fixture below sells nothing at all.</summary>
    private static readonly DateOnly Sunday = new(2026, 9, 20);
    private static readonly DateOnly Monday = new(2026, 9, 21);

    private static readonly TimeOnly SevenAm = new(7, 0);
    private static readonly TimeOnly EightAm = new(8, 0);
    private static readonly TimeOnly SixPm = new(18, 0);

    [Fact]
    public async Task Availability_ShouldPriceEveryHourOfTheDayItIsAskedAbout()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Rates Courts");
        var sut = CreateService(context);

        // Act
        var day = await sut.AvailabilityAsync(floor.Pickleball1, Tuesday, CancellationToken.None);

        // Assert: sixteen hours, the three peak ones dearer, and the platform's
        // per-hour fee alongside each so the page does not have to work it out.
        var slots = day.Value!.Slots;

        using (new AssertionScope())
        {
            day.Value.CourtName.Should().Be("Che court 1 · Pickleball 1");
            slots.Should().HaveCount(16);
            slots.Should().OnlyContain(slot => slot.IsOpen);
            slots.Count(slot => slot.RateKind == "Peak").Should().Be(3);
            slots.Single(slot => slot.StartsAt == SevenAm).Rate.Should().Be(500m);
            slots.Single(slot => slot.StartsAt == SixPm).Rate.Should().Be(600m);
            slots.Should().OnlyContain(slot => slot.PlatformFee == 15m);
        }
    }

    [Fact]
    public async Task Outlook_ShouldCloseADayToWholeHireAsSoonAsOneHourOfItIsGone()
    {
        // Arrange: one hour of Wednesday taken, everything else free.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Outlook Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var outlook = await sut.OutlookAsync(floor.Pickleball1, CancellationToken.None);

        // Assert: the day picker greys Wednesday out rather than letting the
        // customer choose it and be told afterwards. One hour is enough — a day
        // sold open to close has to be whole.
        var window = outlook.Value!;
        var wednesday = window.Single(day => day.Date == Wednesday);
        var tuesday = window.Single(day => day.Date == Tuesday);

        using (new AssertionScope())
        {
            outlook.Succeeded.Should().BeTrue();
            window.Should().HaveCount(BookingWindow.DaysAhead + 1);
            window.First().Date.Should().Be(Today);

            wednesday.OpenHours.Should().Be(15);
            wednesday.TotalHours.Should().Be(16);
            wednesday.CanBeHiredWhole.Should().BeFalse();

            tuesday.OpenHours.Should().Be(16);
            tuesday.CanBeHiredWhole.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Outlook_ShouldBlockTheOtherPartsOfAFloorTakenWhole()
    {
        // Arrange: the whole floor hired for basketball on Thursday. The three
        // pickleball courts marked out on it are that same floor.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Outlook Floor Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Basketball, Thursday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var outlook = await sut.OutlookAsync(floor.Pickleball1, CancellationToken.None);

        // Assert: a picker that only looked at this bookable court would offer
        // Thursday whole, and the booking would then be refused.
        outlook.Value!.Single(day => day.Date == Thursday).CanBeHiredWhole.Should().BeFalse();
    }

    [Fact]
    public async Task Availability_ShouldChargeTheWeekendRateOnASaturday()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Weekend Courts");
        var sut = CreateService(context);

        // Act
        var day = await sut.AvailabilityAsync(floor.Pickleball1, Saturday, CancellationToken.None);

        // Assert: the morning is the weekend rate; the evening is peak, because
        // this venue said peak runs at weekends too and peak is the dearer of
        // the two.
        var slots = day.Value!.Slots;

        using (new AssertionScope())
        {
            slots.Single(slot => slot.StartsAt == SevenAm).RateKind.Should().Be("Weekend");
            slots.Single(slot => slot.StartsAt == SevenAm).Rate.Should().Be(550m);
            slots.Single(slot => slot.StartsAt == SixPm).RateKind.Should().Be("Peak");
            slots.Single(slot => slot.StartsAt == SixPm).Rate.Should().Be(600m);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldBillTheHoursItTookAtTheRatesItQuoted()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Bill Courts");
        var sut = CreateService(context);

        // Act: two standard hours and one peak one.
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.Hourly,
                [
                    new BookingSlotInput(Tuesday, SevenAm),
                    new BookingSlotInput(Tuesday, EightAm),
                    new BookingSlotInput(Tuesday, SixPm)
                ]),
            floor.Customer,
            CancellationToken.None);

        // Assert: 500 + 500 + 600 for the court, 3 x 15 on top for the
        // platform. The fee is shown to the customer and billed to the venue --
        // see docs/platform-fee-strategy.md.
        var detail = booking.Value!;

        using (new AssertionScope())
        {
            detail.BookedHours.Should().Be(3);
            detail.RentalTotal.Should().Be(1600m);
            detail.PlatformFeeTotal.Should().Be(45m);
            detail.Total.Should().Be(1645m);
            detail.Status.Should().Be(nameof(BookingStatus.PendingPayment));
            detail.Slots.Select(slot => slot.RateKind)
                .Should().Equal("Standard", "Standard", "Peak");
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldTakeTheHoursOffTheGrid()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Taken Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var day = await sut.AvailabilityAsync(floor.Pickleball1, Tuesday, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            day.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeFalse();
            day.Value.Slots.Single(slot => slot.StartsAt == EightAm).IsOpen.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldCloseEveryOtherSportOnTheFloor()
    {
        // Arrange: basketball is played across the whole floor.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Whole Floor Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Basketball, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var pickleball = await sut.AvailabilityAsync(floor.Pickleball1, Tuesday, CancellationToken.None);
        var volleyball = await sut.AvailabilityAsync(floor.Volleyball, Tuesday, CancellationToken.None);

        // Assert: a basketball game is played over all of that paint. Selling
        // pickleball alongside it puts two games in one gym.
        using (new AssertionScope())
        {
            pickleball.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeFalse();
            volleyball.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeFalse();
            pickleball.Value.Slots.Single(slot => slot.StartsAt == EightAm).IsOpen.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldLeaveTheOtherPartsOfTheFloorOnSale()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Siblings Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var second = await sut.AvailabilityAsync(floor.Pickleball2, Tuesday, CancellationToken.None);
        var third = await sut.AvailabilityAsync(floor.Pickleball3, Tuesday, CancellationToken.None);
        var basketball = await sut.AvailabilityAsync(floor.Basketball, Tuesday, CancellationToken.None);

        // Assert: three pickleball games at once is the entire point of marking
        // the floor out -- but nobody is playing basketball around the nets.
        using (new AssertionScope())
        {
            second.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeTrue();
            third.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeTrue();
            basketball.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeFalse();
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAnHourSomebodyElseHolds()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Clash Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act: a second customer tries for the same hour.
        var second = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        second.Failure.Should().Be(BookingFailure.SlotTaken);
    }

    [Fact]
    public async Task CreateAsync_ShouldSellAWholeDayAsEveryHourTheCourtIsOpen()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Whole Day Courts");
        var sut = CreateService(context);

        // Act
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.WholeDay,
                [.. AllHours(Tuesday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: thirteen hours at 500 and three at 600, plus the fee on all
        // sixteen.
        using (new AssertionScope())
        {
            booking.Value!.BookedHours.Should().Be(16);
            booking.Value.RentalTotal.Should().Be(8300m);
            booking.Value.PlatformFeeTotal.Should().Be(240m);
            booking.Value.Kind.Should().Be(BookingKind.WholeDay);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAWholeDayWithAnHourAlreadyGone()
    {
        // Arrange: one hour of the day is taken, by pickleball or by anything
        // else on the same floor.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Holed Day Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Basketball, Tuesday, SevenAm),
            Guid.NewGuid(),
            CancellationToken.None);

        // Act
        var whole = await sut.CreateAsync(
            new CreateBookingRequest(floor.Pickleball1, BookingKind.WholeDay, [.. AllHours(Tuesday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: a whole day means the whole day. Quietly handing over one
        // with a hole in it is worse than saying no.
        whole.Failure.Should().Be(BookingFailure.DayNotWhollyAvailable);
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseDaysThatDoNotRunOneAfterAnother()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Gap Days Courts");
        var sut = CreateService(context);

        // Act: Tuesday and Saturday, with the rest of the week in between.
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Tuesday), .. AllHours(Saturday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert
        booking.Failure.Should().Be(BookingFailure.DatesNotConsecutive);
    }

    [Fact]
    public async Task CreateAsync_ShouldCarryARunOverADayWithNothingLeftOnIt()
    {
        // Arrange: every hour of the Thursday sold.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Full Day Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            new CreateBookingRequest(floor.Pickleball1, BookingKind.WholeDay, [.. AllHours(Thursday)]),
            floor.Customer,
            CancellationToken.None);

        // Act
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Wednesday), .. AllHours(Friday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: nothing on the Thursday to be had, so nothing for the run to
        // be broken by.
        using (new AssertionScope())
        {
            booking.Succeeded.Should().BeTrue();
            booking.Value!.BookedHours.Should().Be(32);
            booking.Value.Slots.Select(slot => slot.Date).Should().NotContain(Thursday);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldCarryARunOverADayTheVenueIsShut()
    {
        // Arrange: a venue closed on Sundays.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Sunday Off Courts", DayOfWeek.Sunday);
        var sut = CreateService(context);

        // Act: Friday, Saturday and Monday. Sunday is not in it because there
        // is nothing on Sunday to buy.
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Friday), .. AllHours(Saturday), .. AllHours(Monday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: a venue shut one day a week could otherwise never take a run
        // longer than six days, and never a weekend-to-Monday booking at all.
        using (new AssertionScope())
        {
            booking.Succeeded.Should().BeTrue();
            booking.Value!.BookedHours.Should().Be(48);
            booking.Value.Slots.Select(slot => slot.Date).Should().NotContain(Sunday);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldStillRefuseARunThatJumpsADayItCouldHaveHad()
    {
        // Arrange: the venue is open every day and nothing is taken, so the
        // Thursday being left out was there for the asking.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Jumped Day Courts");
        var sut = CreateService(context);

        // Act
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Wednesday), .. AllHours(Friday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: leaving out a free day is not a run with a hole in it — it is
        // two bookings wearing one name, priced and moved as though they were
        // one.
        booking.Failure.Should().Be(BookingFailure.DatesNotConsecutive);
    }

    [Fact]
    public async Task CreateAsync_ShouldTakeWhatIsLeftOfADaySomebodyElseHasStarted()
    {
        // Arrange: somebody takes 7am on the Thursday.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Taken Day Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Pickleball1, Thursday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act: Wednesday to Friday, taking the fifteen hours Thursday has left.
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [
                    .. AllHours(Wednesday),
                    .. AllHours(Thursday).Where(slot => slot.StartsAt != SevenAm),
                    .. AllHours(Friday)
                ]),
            floor.Customer,
            CancellationToken.None);

        // Assert: 16 + 15 + 16. One booked hour on the Thursday should not cost
        // the customer the other fifteen, nor split their week in two.
        using (new AssertionScope())
        {
            booking.Succeeded.Should().BeTrue();
            booking.Value!.BookedHours.Should().Be(47);
            booking.Value.Slots.Count(slot => slot.Date == Thursday).Should().Be(15);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseARunThatLeavesHoursItCouldHaveHad()
    {
        // Arrange: 7am on the Thursday is gone, and the rest of it is not.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Cherry Picked Courts");
        var sut = CreateService(context);

        await sut.CreateAsync(
            Hourly(floor.Pickleball1, Thursday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act: Wednesday to Friday with the whole Thursday left out.
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Wednesday), .. AllHours(Friday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: a day in a run is all of what it has left. Dropping the
        // fifteen free hours makes this two bookings wearing one name.
        booking.Failure.Should().Be(BookingFailure.DatesNotConsecutive);
    }

    [Fact]
    public async Task CreateAsync_ShouldSellARunOfWholeDays()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Run Of Days Courts");
        var sut = CreateService(context);

        // Act
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Tuesday), .. AllHours(Wednesday)]),
            floor.Customer,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            booking.Value!.BookedHours.Should().Be(32);
            booking.Value.RentalTotal.Should().Be(16600m);
            booking.Value.Slots.Select(slot => slot.Date).Distinct()
                .Should().BeEquivalentTo([Tuesday, Wednesday]);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAnHourThatHasAlreadyGone()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Past Courts");
        var sut = CreateService(context);

        // Act
        var booking = await sut.CreateAsync(
            Hourly(floor.Pickleball1, new DateOnly(2026, 9, 1), SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Assert
        booking.Failure.Should().Be(BookingFailure.DateInThePast);
    }

    [Fact]
    public async Task CancelAsync_ShouldPutTheHoursBackOnSale()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Released Courts");
        var sut = CreateService(context);

        var booking = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        await sut.CancelAsync(booking.Value!.Id, floor.Customer, "Changed our minds", CancellationToken.None);

        // Assert: the hour is free again, and the booking stays on the record
        // rather than being deleted out of the history.
        var day = await sut.AvailabilityAsync(floor.Pickleball1, Tuesday, CancellationToken.None);

        using (new AssertionScope())
        {
            day.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeTrue();
            (await context.Bookings.CountAsync(candidate => candidate.Id == booking.Value.Id))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task Availability_ShouldCloseEveryHourWhileTheCourtIsUnderMaintenance()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Closed Courts");
        var courts = CreateCourtService(context);
        var sut = CreateService(context);

        await courts.SetCourtMaintenanceAsync(
            floor.CourtId,
            new SetMaintenanceRequest(Now, Now.AddDays(7), "Resurfacing"),
            Admin(),
            CancellationToken.None);

        // Act
        var day = await sut.AvailabilityAsync(floor.Pickleball1, Tuesday, CancellationToken.None);
        var booking = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Assert: a closed court shows its hours -- a customer still wants to
        // know what it costs -- but none of them are on sale.
        using (new AssertionScope())
        {
            day.Value!.IsUnderMaintenance.Should().BeTrue();
            day.Value.Slots.Should().HaveCount(16);
            day.Value.Slots.Should().OnlyContain(slot => !slot.IsOpen);
            booking.Failure.Should().Be(BookingFailure.UnderMaintenance);
        }
    }

    [Fact]
    public async Task GetAsync_ShouldNotHandOverSomebodyElsesBooking()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Private Courts");
        var sut = CreateService(context);

        var booking = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Tuesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var stranger = await sut.GetAsync(booking.Value!.Id, Guid.NewGuid(), CancellationToken.None);

        // Assert: answered the same as a booking that does not exist, so an id
        // cannot be probed for whether it belongs to somebody.
        stranger.Failure.Should().Be(BookingFailure.CourtNotFound);
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseADateFurtherAheadThanThePlatformTakes()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Far Ahead Courts");
        var sut = CreateService(context);

        // Act: a day past the window, which the booking page's strip does not
        // even offer.
        var booking = await sut.CreateAsync(
            Hourly(
                floor.Pickleball1,
                DateOnly.FromDateTime(Now.UtcDateTime).AddDays(BookingWindow.DaysAhead + 1),
                SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Assert: a hold costs nothing to make and does not expire yet, so
        // without a ceiling one account could sit on a court for a year.
        booking.Failure.Should().Be(BookingFailure.TooFarAhead);
    }

    [Fact]
    public async Task CreateAsync_ShouldTakeTheLastDayOfTheWindow()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Edge Of Window Courts");
        var sut = CreateService(context);

        // Act: the thirtieth day itself, which the strip does offer.
        var booking = await sut.CreateAsync(
            Hourly(
                floor.Pickleball1,
                DateOnly.FromDateTime(Now.UtcDateTime).AddDays(BookingWindow.DaysAhead),
                SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Assert: the boundary is inclusive, so what the page shows and what the
        // server takes are the same set of days.
        booking.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Availability_ShouldCloseTodaysHoursThatHaveAlreadyBegun()
    {
        // Arrange: the clock is 09:00 UTC, which is 5pm where the venue is.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Today Courts");
        var sut = CreateService(context);

        // Act
        var day = await sut.AvailabilityAsync(floor.Pickleball1, Today, CancellationToken.None);

        // Assert: 6am to 5pm has gone; 6pm to 10pm is still sellable. Times are
        // read on the venue's wall clock -- in Manila a UTC clock is eight hours
        // behind, and reading "now" off the server would leave this morning on
        // sale until the middle of the evening.
        var slots = day.Value!.Slots;

        using (new AssertionScope())
        {
            slots.Should().HaveCount(16);
            slots.Where(slot => slot.StartsAt <= new TimeOnly(17, 0))
                .Should().OnlyContain(slot => slot.HasPassed && !slot.IsOpen);
            slots.Where(slot => slot.StartsAt > new TimeOnly(17, 0))
                .Should().OnlyContain(slot => !slot.HasPassed && slot.IsOpen);
            slots.Count(slot => slot.IsOpen).Should().Be(4);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseAnHourOfTodayThatHasAlreadyStarted()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking This Morning Courts");
        var sut = CreateService(context);

        // Act: this morning, asked for this evening.
        var booking = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Today, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Assert: the date check alone let this through -- today is not before
        // today -- and an hour that has been and gone was sellable all day.
        booking.Failure.Should().Be(BookingFailure.SlotTaken);
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseTodayAsAWholeDay()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Today Whole Courts");
        var sut = CreateService(context);

        // Act
        var booking = await sut.CreateAsync(
            new CreateBookingRequest(floor.Pickleball1, BookingKind.WholeDay, [.. AllHours(Today)]),
            floor.Customer,
            CancellationToken.None);

        // Assert: a day sold open to close has to still have its opening in it.
        // The booking page greys today out for the same reason.
        booking.Failure.Should().Be(BookingFailure.DayNotWhollyAvailable);
    }

    [Fact]
    public async Task ListForCustomerAsync_ShouldSayWhenAMoveIsWaitingOnTheVenue()
    {
        // Arrange: a booking with a move asked for and nothing to pay, so it is
        // sitting with the venue.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Waiting Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2),
            CancellationToken.None);

        // Act
        var mine = await sut.ListForCustomerAsync(floor.Customer, CancellationToken.None);

        // Assert: the card still shows the old court, because the booking has
        // not moved. Without this the customer sees the court they asked to
        // leave and no sign the request landed — and asks again.
        var card = mine.Single(row => row.Id == booking);

        using (new AssertionScope())
        {
            card.PendingMove.Should().NotBeNull();
            card.PendingMove!.Status.Should().Be(MoveRequestStatus.AwaitingConfirmation);
            card.PendingMove.BalanceDue.Should().Be(0m);
            card.PendingMove.RaisedByVenue.Should().BeFalse();
            card.PendingMove.ToCourtName.Should().NotBeNullOrWhiteSpace();

            // And it cannot be moved again while one is open, so the button
            // does not offer what the server would refuse.
            card.CanBeMoved.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ListForCustomerAsync_ShouldCarryNoPendingMoveForAnUntouchedBooking()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Untouched Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var mine = await sut.ListForCustomerAsync(floor.Customer, CancellationToken.None);

        // Assert: a panel that says "waiting" over a booking nobody has touched
        // is worse than no panel at all.
        var card = mine.Single(row => row.Id == booking);

        using (new AssertionScope())
        {
            card.PendingMove.Should().BeNull();
            card.CanBeMoved.Should().BeTrue();
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseACourtMarkedOutForAnotherSport()
    {
        // Arrange: a pickleball booking, and the same floor set up for
        // basketball as well — a real arrangement, and the one that makes this
        // possible at all.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Wrong Sport Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Basketball),
            CancellationToken.None);

        // Assert: a booking records the sport as it was named and priced when
        // it was sold. Moving it onto a basketball court would leave a row
        // still saying "Pickleball", charged at the pickleball rate, against
        // hours nobody can play pickleball in.
        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.NotTheSameOffering);

            (await context.BookingMoveRequests.CountAsync(row => row.BookingId == booking))
                .Should().Be(0);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseACourtAtAnotherVenue()
    {
        // Arrange: the same sport, but in a different building.
        await using var context = database.CreateContext();
        var here = await FloorAsync(context, "This Venue Courts");
        var elsewhere = await FloorAsync(context, "Another Venue Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, here, Wednesday, SevenAm);

        // Act
        var asked = await sut.MoveAsync(
            booking,
            here.Customer,
            new MoveBookingRequest(elsewhere.Pickleball1),
            CancellationToken.None);

        // Assert: the booking carries the venue's name as it was sold, and the
        // customer would be told to turn up somewhere the booking does not
        // name. A move is a change of floor, not a change of agreement.
        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.NotTheSameOffering);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRaiseARequestRatherThanMoveTheBooking()
    {
        // Arrange: a confirmed booking on one part of the floor.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Moving Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: ask to move to another part of the same floor.
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2),
            CancellationToken.None);

        // Assert: the booking has not moved. A move that wants paying for needs
        // a clock on it, and putting a settled booking back into a paying state
        // would let that clock expire something already paid for.
        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        var request = await context.BookingMoveRequests
            .AsNoTracking()
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);
            request.ToBookableCourtId.Should().Be(floor.Pickleball2);
            // Same floor, same sport, same hour: nothing to pay.
            request.BalanceDue.Should().Be(0m);
            request.Status.Should().Be(MoveRequestStatus.AwaitingConfirmation);
        }
    }

    [Fact]
    public async Task ConfirmMoveAsync_ShouldPutTheBookingOnTheNewCourt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Confirmed Move Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2),
            CancellationToken.None);

        // Act
        var confirmed = await sut.ConfirmMoveAsync(booking, Guid.NewGuid(), CancellationToken.None);

        // Assert: on the new court, counted against the customer's allowance,
        // and the hour it left is free again.
        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            confirmed.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball2);
            stored.MoveCount.Should().Be(1);
            stored.Slots.Should().ContainSingle()
                .Which.BookableCourtId.Should().Be(floor.Pickleball2);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldChargeTheDifferenceForADearerCourt()
    {
        // Arrange: the same floor sold as basketball costs more per hour than
        // one of the three pickleball courts marked out on it.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer),
            CancellationToken.None);

        // Assert: 900 against the 500 already paid, and the platform fee is not
        // charged twice because the hours have not changed.
        var request = await context.BookingMoveRequests
            .AsNoTracking()
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeTrue();
            request.BalanceDue.Should().Be(400m);
            request.Status.Should().Be(MoveRequestStatus.AwaitingPayment);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldAskForNothingWhenTheNewCourtIsCheaper()
    {
        // Arrange: this time the whole floor is cheaper than the part.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Cheaper Courts");
        var sut = CreateService(context);
        var cheaper = await SecondCourtAsync(context, floor, pickleballRate: 300m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(cheaper),
            CancellationToken.None);

        // Assert: nothing owed, and nothing given back either. There are no
        // refunds — the customer keeps the booking and pays no more.
        var request = await context.BookingMoveRequests
            .AsNoTracking()
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            request.BalanceDue.Should().Be(0m);
            request.Status.Should().Be(MoveRequestStatus.AwaitingConfirmation);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldHoldTheCourtItIsMovingOnto()
    {
        // Arrange: a booking asking to move onto the whole floor.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Held Move Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Basketball),
            CancellationToken.None);

        // Act: somebody else tries for the same hour on that floor.
        var day = await sut.AvailabilityAsync(floor.Volleyball, Wednesday, CancellationToken.None);

        // Assert: a move waiting to land holds its hours exactly as an unpaid
        // booking holds the hours it is waiting to pay for. Without this the
        // court somebody is part way through paying an upgrade for is still on
        // sale to the next person.
        day.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseASecondRequestWhileOneIsWaiting()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "One At A Time Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2),
            CancellationToken.None);

        // Act
        var again = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball3),
            CancellationToken.None);

        // Assert: two at once and the customer and the venue can be sending the
        // same booking to different courts.
        again.Failure.Should().Be(BookingFailure.MoveAlreadyRequested);
    }

    [Fact]
    public async Task MoveByVenueAsync_ShouldMoveAtOnceAndRecordWhy()
    {
        // Arrange: the court has a problem and the players are on it.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Flooded Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);
        var attendant = Guid.NewGuid();

        // Act
        var moved = await sut.MoveByVenueAsync(
            booking,
            attendant,
            floor.Pickleball2,
            "Court 1 flooded after the morning rain.",
            null,
            CancellationToken.None);

        // Assert: immediate, and it does not spend the customer's allowance —
        // the venue's flooded court is not the customer's doing.
        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == booking);

        var request = await context.BookingMoveRequests
            .AsNoTracking()
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball2);
            stored.MoveCount.Should().Be(0);
            request.Status.Should().Be(MoveRequestStatus.Completed);
            request.Initiator.Should().Be(MoveInitiator.Attendant);
            request.Reason.Should().Contain("flooded");
        }
    }

    [Fact]
    public async Task MoveByVenueAsync_ShouldRefuseADearerCourtWithoutAWaiver()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Unwaived Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var moved = await sut.MoveByVenueAsync(
            booking,
            Guid.NewGuid(),
            dearer,
            "Lights failed on court 1.",
            null,
            CancellationToken.None);

        // Assert: an attendant cannot take money from somebody who is not in
        // the conversation. Either the customer is asked to upgrade, or the
        // venue absorbs it and says who decided that.
        moved.Failure.Should().Be(BookingFailure.MoveNotPaid);
    }

    [Fact]
    public async Task MoveByVenueAsync_ShouldMoveToADearerCourtWhenTheVenueWaivesTheDifference()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Waived Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);
        var attendant = Guid.NewGuid();

        // Act
        var moved = await sut.MoveByVenueAsync(
            booking,
            attendant,
            dearer,
            "Lights failed on court 1.",
            "Our fault, so we are absorbing the difference.",
            CancellationToken.None);

        // Assert: two reasons, because they answer different questions — why
        // the booking moved, and who decided the customer would not pay.
        var request = await context.BookingMoveRequests
            .AsNoTracking()
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeTrue();
            request.BalanceDue.Should().Be(0m);
            request.WaivedByUserId.Should().Be(attendant);
            request.WaiverReason.Should().Contain("absorbing");
            request.Reason.Should().Contain("Lights");
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseAnHourSomebodyElseHolds()
    {
        // Arrange: a booking on the first pickleball court, and somebody else
        // already on the second at the same hour. The three run side by side,
        // so both can stand at once — which is what makes the second one a
        // court this booking cannot move onto.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Contested Move Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var alreadyThere = await sut.CreateAsync(
            Hourly(floor.Pickleball2, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            alreadyThere.Succeeded.Should().BeTrue();
            asked.Failure.Should().Be(BookingFailure.SlotTaken);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseAHoldNobodyHasPaidFor()
    {
        // Arrange: taken and not paid for.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Unpaid Move Courts");
        var sut = CreateService(context);
        var booking = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var asked = await sut.MoveAsync(
            booking.Value!.Id,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2),
            CancellationToken.None);

        // Assert: a hold that has not been paid for needs no moving. Let it
        // lapse and book the other court.
        asked.Failure.Should().Be(BookingFailure.NotMovable);
    }

    /// <summary>A booking the venue has checked and confirmed.</summary>
    /// <summary>
    /// Puts a different rate on the whole floor sold as basketball, so a move
    /// between the parts and the whole has a difference to settle.
    /// </summary>
    /// <summary>
    /// A second court in the same building, marked out for pickleball at its
    /// own rate, and the id of its first division.
    ///
    /// This is what an upgrade is: a better court for the same game. The three
    /// pickleball divisions of one court share a single price row, so a dearer
    /// pickleball court has to be a different court — repricing the sport this
    /// booking is already on would move the booking's own price with it.
    /// </summary>
    private static async Task<Guid> SecondCourtAsync(
        AppDbContext context,
        Floor floor,
        decimal pickleballRate)
    {
        var courts = CreateCourtService(context);
        var pickleball = await SportIdAsync(context, "pickleball");

        var here = await context.Courts
            .AsNoTracking()
            .Where(court => court.Id == floor.CourtId)
            .Select(court => new { court.FacilityOwnerId, court.FacilityId })
            .SingleAsync();

        var created = await courts.CreateAsync(
            new CreateCourtRequest(
                here.FacilityOwnerId,
                here.FacilityId,
                null,
                new CourtInput(
                    "Che court 2",
                    10,
                    "The far court.",
                    [new CourtSportInput(pickleball, 3)],
                    pickleball,
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
                    [])),
            Admin(),
            CancellationToken.None);

        await courts.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [new SportPricingInput(pickleball, pickleballRate, null, null, null)],
                new PeakWindowInput(new TimeOnly(17, 0), new TimeOnly(20, 0), true, true),
                "Opening rates"),
            Admin(),
            CancellationToken.None);

        return await context.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.CourtId == created.Value.CourtId && unit.DivisionNumber == 1)
            .Select(unit => unit.Id)
            .SingleAsync();
    }

    private static async Task<Guid> ConfirmedAsync(
        AppDbContext context,
        BookingService bookings,
        Floor floor,
        DateOnly date,
        TimeOnly hour)
    {
        var created = await bookings.CreateAsync(
            Hourly(floor.Pickleball1, date, hour),
            floor.Customer,
            CancellationToken.None);

        var booking = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        booking.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            Now);
        booking.SubmitForVerification(Now);
        booking.Confirm(Now);
        await context.SaveChangesAsync();

        return booking.Id;
    }

    // ------------------------------------------------------------- the set-up

    private static CreateBookingRequest Hourly(Guid bookableCourtId, DateOnly date, TimeOnly startsAt) =>
        new(bookableCourtId, BookingKind.Hourly, [new BookingSlotInput(date, startsAt)]);

    /// <summary>Every hour the venue is open: 6am to 10pm.</summary>
    private static IEnumerable<BookingSlotInput> AllHours(DateOnly date) =>
        Enumerable.Range(6, 16).Select(hour => new BookingSlotInput(date, new TimeOnly(hour, 0)));

    /// <summary>
    /// One floor sold five ways, priced: basketball and volleyball whole,
    /// pickleball three across, 500 standard / 600 peak 5-8pm / 550 weekend.
    /// </summary>
    private static async Task<Floor> FloorAsync(
        AppDbContext context,
        string facilityName,
        DayOfWeek? shutOn = null)
    {
        var courts = CreateCourtService(context);
        var basketball = await SportIdAsync(context, "basketball");
        var volleyball = await SportIdAsync(context, "volleyball");
        var pickleball = await SportIdAsync(context, "pickleball");

        var user = new User($"booker-{Guid.NewGuid():N}@example.com", "Court Owner", null);
        user.SetPasswordHash("hash");
        context.Users.Add(user);
        var owner = new FacilityOwner(user.Id, "Court Ventures", "billing@example.com", null);
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
                    "Che court 1",
                    10,
                    "The near court.",
                    [
                        new CourtSportInput(basketball, 1),
                        new CourtSportInput(volleyball, 1),
                        new CourtSportInput(pickleball, 3)
                    ],
                    basketball,
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
                    [])),
            Admin(),
            CancellationToken.None);

        await courts.UpdatePricingAsync(
            created.Value!.CourtId,
            new UpdateCourtPricingRequest(
                [
                    new SportPricingInput(basketball, 500m, 600m, 550m, 700m),
                    new SportPricingInput(volleyball, 500m, 600m, 550m, 700m),
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

        return new Floor(
            created.Value.CourtId,
            Unit(basketball, 1),
            Unit(volleyball, 1),
            Unit(pickleball, 1),
            Unit(pickleball, 2),
            Unit(pickleball, 3),
            Guid.NewGuid());
    }

    private sealed record Floor(
        Guid CourtId,
        Guid Basketball,
        Guid Volleyball,
        Guid Pickleball1,
        Guid Pickleball2,
        Guid Pickleball3,
        Guid Customer);

    private static BookingService CreateService(AppDbContext context) => new(
        context,
        Assets(),
        new SilentNotifier(),
        new FixedTimeProvider(Now),
        NullLogger<BookingService>.Instance);

    private static CloudinaryAssetService Assets() => new(
        Options.Create(new CloudinaryOptions
        {
            CloudName = "icyplay-test",
            ApiKey = "123456789012345",
            ApiSecret = "test-api-secret"
        }),
        new FixedTimeProvider(Now));

    /// <summary>
    /// Letters are not what these tests are about, and a real sender here would
    /// make them depend on a mail provider being reachable.
    /// </summary>
    private sealed class SilentNotifier : IBookingNotifier
    {
        public Task PaymentSubmittedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;

        public Task BookingConfirmedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;
    }

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

    private static AuditActor Admin() => new(Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private static async Task<Guid> SportIdAsync(AppDbContext context, string key) =>
        await context.Sports.Where(sport => sport.Key == key).Select(sport => sport.Id).SingleAsync();

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
}
