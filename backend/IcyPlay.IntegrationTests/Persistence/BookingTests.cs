using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Email;
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

    // A link on the platform's own Cloudinary account, which is the only kind
    // a receipt is allowed to be.
    private const string Receipt =
        "https://res.cloudinary.com/icyplay-test/image/upload/v1/upgrade-receipt.jpg";

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
    public async Task CreateAsync_ShouldRefuseARunThroughADaySomebodyElseHasStarted()
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

        // Assert: refused. This used to sell — 16 + 15 + 16 — on the reasoning
        // that one booked hour should not cost the customer the other fifteen
        // nor split their week in two. What it sold instead was a "week" with
        // an hour missing from the middle of it, at the price of three whole
        // days. A run is whole days; a week with a hole in it is two bookings
        // that say what they are, and the hour itself is still there to book.
        booking.Failure.Should().Be(BookingFailure.DayNotWhollyAvailable);
    }

    [Fact]
    public async Task CreateAsync_ShouldPassOverADaySomebodyElseHasPartOf()
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

        // Assert: two days, and the Thursday is not among them.
        //
        // This was refused, on the reasoning that dropping the Thursday's
        // fifteen free hours made the booking two bookings wearing one name.
        // The reasoning held and the outcome was still wrong: what the
        // customer got instead WAS two bookings — two holds, two clocks, two
        // receipts — and paying one while the other lapsed left the venue with
        // half a trip. A run may now pass over a day it could not have had
        // whole. It is not booked and not charged for, and the screen that
        // offers the run says which days it is selling.
        //
        // The Thursday's fifteen free hours are still there to book by the
        // hour, which is the honest way to sell what is left of a day.
        using (new AssertionScope())
        {
            booking.Succeeded.Should().BeTrue();
            booking.Value!.BookedHours.Should().Be(32);
            booking.Value.Slots.Select(slot => slot.Date).Distinct()
                .Should().BeEquivalentTo([Wednesday, Friday]);
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldPassOverADayThatIsWhollyGone()
    {
        // Arrange: somebody has the whole Thursday.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Booking Gone Day Courts");
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

        // Assert: the same answer as a day with an hour gone, which is the
        // point — the customer cannot see the difference between a day that is
        // wholly taken and one that is merely unbuyable, and should not have to.
        using (new AssertionScope())
        {
            booking.Succeeded.Should().BeTrue();
            booking.Value!.BookedHours.Should().Be(32);
            booking.Value.Slots.Select(slot => slot.Date).Distinct()
                .Should().BeEquivalentTo([Wednesday, Friday]);
        }
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
    public async Task ListForCustomerAsync_ShouldSayABookingStillToComeIsNotInPlay()
    {
        // Arrange: a booking on a day that has not arrived.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Not Yet Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var mine = await sut.ListForCustomerAsync(floor.Customer, CancellationToken.None);

        // Assert: the move screen reads this to decide whether to offer dates
        // at all, so saying a booking is in play when it has not started takes
        // away a choice the customer is entitled to.
        var card = mine.Single(row => row.Id == booking);

        using (new AssertionScope())
        {
            card.IsInPlay.Should().BeFalse();
            card.CanBeMoved.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ListForCustomerAsync_ShouldSayABookingUnderWayIsInPlay()
    {
        // Arrange: a booking whose hour has begun. Seven in the morning on the
        // venue's clock, read at eight.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Under Way Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Wednesday at 08:00 in Manila is 00:00 UTC on the same day.
        var duringTheGame = new DateTimeOffset(
            Wednesday.ToDateTime(new TimeOnly(0, 0)),
            TimeSpan.Zero);

        var later = CreateService(context, duringTheGame);

        // Act
        var mine = await later.ListForCustomerAsync(floor.Customer, CancellationToken.None);

        // Assert: the game is on. The court can still change — a floodlight
        // fails and they carry on next door — but when it is cannot.
        var card = mine.Single(row => row.Id == booking);

        card.IsInPlay.Should().BeTrue();
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
            new MoveBookingRequest(floor.Basketball, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: a booking records the sport as it was named and priced when
        // it was sold. Moving it onto a basketball court would leave a row
        // still saying "Pickleball", charged at the pickleball rate, against
        // hours nobody can play pickleball in.
        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.NotTheSameOffering);
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
            new MoveBookingRequest(elsewhere.Pickleball1, Reason: MoveReason.ScheduleChanged),
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
    public async Task MoveAsync_ShouldWaitForTheVenueBeforeMoving()
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
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: asked, not done. The booking stays where it is and nothing is
        // counted until somebody at the desk agrees to it.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);
        var waiting = await context.BookingUpgradeRequests
            .AsNoTracking()
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);

            // Straight to the desk: there is nothing to pay.
            waiting.Status.Should().Be(UpgradeStatus.AwaitingApproval);
            waiting.BalanceDue.Should().Be(0m);
            waiting.ToBookableCourtId.Should().Be(floor.Pickleball2);

            // The page says a move is waiting, and offers no second one.
            asked.Value!.UpgradeStatus.Should().Be(UpgradeStatus.AwaitingApproval);
            asked.Value.UpgradeBalanceDue.Should().Be(0m);
            asked.Value.CanBeMoved.Should().BeFalse();
            asked.Value.MovesLeft.Should().Be(BookingMove.DefaultLimit);
        }
    }

    [Fact]
    public async Task ApproveUpgradeAsync_ShouldMoveAFreeMoveWithoutAReceipt()
    {
        // Arrange: a free move, asked for and waiting.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Free Move Approved Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var moved = await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged));

        // Assert: moved, counted, and nothing paid — there was nothing to pay.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);
        var record = await context.BookingMoves.AsNoTracking().SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball2);
            stored.MoveCount.Should().Be(1);
            moved.Value!.MovesLeft.Should().Be(BookingMove.DefaultLimit - 1);
            moved.Value.UpgradeStatus.Should().BeNull();
            record.Kind.Should().Be(MoveKind.Free);

            // The hours are the same ones, on the new court.
            stored.Slots.Should().ContainSingle();
            stored.Slots.Single().BookableCourtId.Should().Be(floor.Pickleball2);
        }
    }

    [Fact]
    public async Task DeclineUpgradeAsync_ShouldLeaveAFreeMoveUncounted()
    {
        // Arrange: a free move, asked for and waiting.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Free Move Declined Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        var moveId = await context.BookingUpgradeRequests
            .Where(row => row.BookingId == booking)
            .Select(row => row.Id)
            .SingleAsync();
        var ownerUserId = await OwnerUserIdAsync(context, floor);
        context.ChangeTracker.Clear();

        // Act
        var declined = await CreateDeskService(context).DeclineUpgradeAsync(
            ownerUserId,
            moveId,
            "Court 2 is booked for a league that night.",
            new AuditActor(ownerUserId, UserRoleName.FacilityOwner),
            CancellationToken.None);

        // Assert: the booking is where it was, and a move the venue turned down
        // is not one the customer has had — they can ask again.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);
        var detail = await sut.GetAsync(booking, floor.Customer, CancellationToken.None);

        using (new AssertionScope())
        {
            declined.Succeeded.Should().BeTrue();
            declined.Value!.Status.Should().Be(UpgradeStatus.Declined);
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);
            detail.Value!.MovesLeft.Should().Be(BookingMove.DefaultLimit);
            detail.Value.CanBeMoved.Should().BeTrue();
            (await context.BookingMoves.AnyAsync(row => row.BookingId == booking)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldHoldTheHoursItAsksForWhileTheVenueDecides()
    {
        // Arrange: a move to the second court at seven, waiting on the desk.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Held For The Desk Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Act: somebody else tries to book that very hour.
        var day = await sut.AvailabilityAsync(floor.Pickleball2, Wednesday, CancellationToken.None);
        var second = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball3, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: not on sale while the desk decides, and one move at a time.
        using (new AssertionScope())
        {
            day.Value!.Slots.Single(slot => slot.StartsAt == SevenAm).IsOpen.Should().BeFalse();
            second.Failure.Should().Be(BookingFailure.MoveAlreadyRequested);
        }
    }

    /// <summary>
    /// A free move writes to the venue when it is asked for and to the customer
    /// when it is answered, through its own templates — not the upgrade's.
    /// </summary>
    [Fact]
    public async Task MoveAsync_ShouldWriteToTheVenueThenToTheCustomerWithTheAnswer()
    {
        // Arrange: two moves asked for, one approved and one declined.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Move Letters Courts");

        // A customer with an account, because the letters are written to them.
        var customer = new User($"mover-{Guid.NewGuid():N}@example.com", "Maria Santos", null);
        context.Users.Add(customer);
        await context.SaveChangesAsync();
        floor = floor with { Customer = customer.Id };
        var mail = new CapturingEmailSender();
        var notifier = new BookingNotifier(
            context,
            mail,
            Options.Create(new BookingNotificationOptions
            {
                BookingUrl = "https://icyplay.test/bookings",
                SupportEmail = "help@icyplay.test"
            }),
            new FixedTimeProvider(Now),
            NullLogger<BookingNotifier>.Instance);
        var sut = new BookingService(
            context,
            Assets(),
            notifier,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now),
            NullLogger<BookingService>.Instance);
        var desk = new DeskService(
            context,
            notifier,
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now),
            NullLogger<DeskService>.Instance);
        var ownerUserId = await OwnerUserIdAsync(context, floor);
        var actor = new AuditActor(ownerUserId, UserRoleName.FacilityOwner);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: asked and approved.
        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.Weather),
            CancellationToken.None);
        var first = await context.BookingUpgradeRequests.Where(row => row.BookingId == booking).Select(row => row.Id).SingleAsync();
        context.ChangeTracker.Clear();
        await desk.ApproveUpgradeAsync(ownerUserId, first, actor, CancellationToken.None);
        context.ChangeTracker.Clear();

        // Then asked again, and declined.
        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball3, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);
        var second = await context.BookingUpgradeRequests
            .Where(row => row.BookingId == booking && row.Id != first)
            .Select(row => row.Id)
            .SingleAsync();
        context.ChangeTracker.Clear();
        await desk.DeclineUpgradeAsync(ownerUserId, second, "League night", actor, CancellationToken.None);

        // Assert
        var sent = mail.Sent.Select(letter => letter.TemplateKey).ToArray();
        var asked = mail.Sent.First(letter => letter.TemplateKey == EmailTemplateKey.BookingMoveRequested);
        var approved = mail.Sent.Single(letter => letter.TemplateKey == EmailTemplateKey.BookingMoveApproved);
        var declined = mail.Sent.Single(letter => letter.TemplateKey == EmailTemplateKey.BookingMoveDeclined);

        using (new AssertionScope())
        {
            sent.Should().Equal(
                EmailTemplateKey.BookingMoveRequested,
                EmailTemplateKey.BookingMoveApproved,
                EmailTemplateKey.BookingMoveRequested,
                EmailTemplateKey.BookingMoveDeclined);

            asked.Variables["move_reason"].Should().Be(MoveReason.Label(MoveReason.Weather));
            asked.Variables["from_hours"].Should().Be("7:00 AM – 8:00 AM");
            asked.RecipientEmail.Should().NotBe(approved.RecipientEmail);

            // Approved: the court it is on now, and the one it left.
            approved.Variables["court_name"].Should().NotBe(approved.Variables["from_court_name"]);

            // Declined: still where the approved move put it, nothing about
            // money on a free move, and the desk's reason.
            declined.Variables["court_name"].Should().Be(approved.Variables["court_name"]);
            declined.Variables["decline_reason"].Should().Be("League night");
            declined.Variables["money_note"].Should().Be(string.Empty);
            declined.Variables["booking_url"].Should().Be($"https://icyplay.test/bookings/{booking}");
        }
    }

    /// <summary>
    /// A letter to the venue goes to everybody working its desk — the owner
    /// and each attendant who has set up their account — not the owner alone.
    /// </summary>
    [Fact]
    public async Task MoveAsync_ShouldWriteToEveryoneAtTheDesk()
    {
        // Arrange: an owner, one attendant on the desk, one invited who never
        // signed up, and one who has been taken off.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Whole Desk Letters Courts");
        var facilityId = await context.Courts
            .Where(court => court.Id == floor.CourtId)
            .Select(court => court.FacilityId)
            .SingleAsync();

        User Person(string who, bool verified)
        {
            var user = new User($"{who}-{Guid.NewGuid():N}@example.com", who, null);

            if (verified)
            {
                user.MarkEmailVerified(Now);
            }

            context.Users.Add(user);

            return user;
        }

        var customer = Person("Maria Santos", verified: true);
        var onDesk = Person("Ana Reyes", verified: true);
        var invited = Person("Ben Cruz", verified: false);
        var gone = Person("Carlo Lim", verified: true);
        context.FacilityAttendants.Add(new FacilityAttendant(facilityId, onDesk.Id, Now));
        context.FacilityAttendants.Add(new FacilityAttendant(facilityId, invited.Id, Now));
        var retired = new FacilityAttendant(facilityId, gone.Id, Now);
        retired.Retire(Now);
        context.FacilityAttendants.Add(retired);
        await context.SaveChangesAsync();
        floor = floor with { Customer = customer.Id };

        var ownerEmail = await context.BookableCourts
            .Where(unit => unit.Id == floor.Pickleball1)
            .Select(unit => unit.Court.Facility.FacilityOwner.User.Email)
            .SingleAsync();

        var mail = new CapturingEmailSender();
        var sut = new BookingService(
            context,
            Assets(),
            new BookingNotifier(
                context,
                mail,
                Options.Create(new BookingNotificationOptions { SupportEmail = "help@icyplay.test" }),
                new FixedTimeProvider(Now),
                NullLogger<BookingNotifier>.Instance),
            new AuditLogger(context, new FixedTimeProvider(Now)),
            new FixedTimeProvider(Now),
            NullLogger<BookingService>.Instance);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.Weather),
            CancellationToken.None);

        // Assert: one letter each, addressed by name, and nobody who cannot
        // open the queue it points to.
        var letters = mail.Sent
            .Where(letter => letter.TemplateKey == EmailTemplateKey.BookingMoveRequested)
            .ToArray();

        using (new AssertionScope())
        {
            letters.Select(letter => letter.RecipientEmail)
                .Should().BeEquivalentTo([ownerEmail, onDesk.Email]);
            letters.Single(letter => letter.RecipientEmail == onDesk.Email)
                .Variables["recipient_name"].Should().Be("Ana Reyes");
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseInsideTheVenuesNotice()
    {
        // Arrange: a booking at seven on Wednesday, thirty-eight hours away, at
        // a venue that stops taking moves two days before.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Notice Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var ownerUserId = await OwnerUserIdAsync(context, floor);
        var owner = await context.FacilityOwners.SingleAsync(row => row.UserId == ownerUserId);
        owner.SetMoveNotice(2, Now);
        await context.SaveChangesAsync();

        // Act
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);
        var options = await sut.MoveOptionsAsync(
            booking,
            floor.Customer,
            new MoveOptionsRequest(),
            CancellationToken.None);
        var detail = await sut.GetAsync(booking, floor.Customer, CancellationToken.None);

        // Assert: too close for the hours it gives back to be sold again, and
        // the page is told so rather than offering a button that only refuses.
        using (new AssertionScope())
        {
            asked.Failure.Should().Be(BookingFailure.TooLateToMove);
            options.Failure.Should().Be(BookingFailure.TooLateToMove);
            detail.Value!.CanBeMoved.Should().BeFalse();
            detail.Value.IsInsideMoveNotice.Should().BeTrue();
            detail.Value.MoveNoticeDays.Should().Be(2);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldOnlyChangeCourtOnceTheBookingHasStarted()
    {
        // Arrange: two hours from seven, asked about at half past seven — well
        // inside any notice, and under way.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Court Only Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm, hours: 2);

        var underWay = CreateService(
            context,
            new DateTimeOffset(Tuesday.ToDateTime(new TimeOnly(23, 30)), TimeSpan.Zero));

        // Act
        var otherHours = await underWay.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(
                floor.Pickleball2,
                [new BookingSlotInput(Wednesday, new TimeOnly(10, 0))],
                Reason: MoveReason.CourtProblem),
            CancellationToken.None);
        var otherCourt = await underWay.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.CourtProblem),
            CancellationToken.None);

        // Assert: the notice does not stop what is left of it changing court,
        // but its hours are being played and stay as they are. Still asked of
        // the desk.
        using (new AssertionScope())
        {
            otherHours.Failure.Should().Be(BookingFailure.CourtOnlyOnceStarted);
            otherCourt.Succeeded.Should().BeTrue();
            otherCourt.Value!.UpgradeStatus.Should().Be(UpgradeStatus.AwaitingApproval);
        }
    }

    /// <summary>
    /// A move is not made without a reason from the list, and Other is not a
    /// way of saying nothing. Asked of the customer because the venue's report
    /// counts the answers — a move with no answer is one it cannot count.
    /// </summary>
    [Theory]
    [InlineData(null, 0, BookingFailure.MoveReasonRequired)]
    [InlineData("Bored", 0, BookingFailure.MoveReasonRequired)]
    [InlineData(MoveReason.Other, 0, BookingFailure.MoveReasonNoteRequired)]
    [InlineData(MoveReason.Weather, MoveReason.NoteLimit + 1, BookingFailure.MoveReasonNoteTooLong)]
    public async Task MoveAsync_ShouldAskWhyBeforeMoving(
        string? reason,
        int noteLength,
        BookingFailure expected)
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, $"Reason Asked Courts {(int)expected}");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(
                floor.Pickleball2,
                Reason: reason,
                ReasonNote: noteLength == 0 ? "   " : new string('x', noteLength)),
            CancellationToken.None);

        // Assert: refused, and nothing moved or was counted.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            asked.Failure.Should().Be(expected);
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);
            (await context.BookingMoves.AnyAsync(row => row.BookingId == booking)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldCountTheMoveWithItsReason()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Reason Counted Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: Other, with the few words it asks for, and the venue agrees.
        var asked = await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.Other, ReasonNote: "  Friend is late  "));

        // Assert: one row for the report, carrying where it went and why, and
        // the history the desk reads says why too.
        context.ChangeTracker.Clear();

        var moved = await context.BookingMoves.AsNoTracking().SingleAsync(row => row.BookingId == booking);
        var history = await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(row => row.EntityId == booking && row.Action == AuditAction.BookingMoveRequested);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeTrue();
            moved.Kind.Should().Be(MoveKind.Free);
            moved.Reason.Should().Be(MoveReason.Other);
            moved.ReasonNote.Should().Be("Friend is late");
            moved.MovedAt.Should().Be(Now);
            moved.MovedByUserId.Should().Be(floor.Customer);
            moved.ToCourtName.Should().NotBeNullOrEmpty();
            history.NewValuesJson.Should().Contain("Reason: Other").And.Contain("Friend is late");
        }
    }

    [Fact]
    public async Task RequestUpgradeAsync_ShouldAskWhyBeforeWritingItDown()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Reason Asked Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            asked.Failure.Should().Be(BookingFailure.MoveReasonRequired);
            (await context.BookingUpgradeRequests.AnyAsync(row => row.BookingId == booking)).Should().BeFalse();
        }
    }

    /// <summary>
    /// The reason is asked when the upgrade is, and counted when it goes
    /// through. The desk approving it is not who knows why, so it is carried.
    /// </summary>
    [Fact]
    public async Task ApproveUpgradeAsync_ShouldCountTheMoveWithTheReasonTheCustomerGave()
    {
        // Arrange: an upgrade asked for in the rain and paid for.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Reason Counted Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.Weather),
            CancellationToken.None);
        await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        var upgradeId = await context.BookingUpgradeRequests
            .Where(row => row.BookingId == booking)
            .Select(row => row.Id)
            .SingleAsync();
        var ownerUserId = await OwnerUserIdAsync(context, floor);
        context.ChangeTracker.Clear();

        // Act
        var approved = await CreateDeskService(context).ApproveUpgradeAsync(
            ownerUserId,
            upgradeId,
            new AuditActor(ownerUserId, UserRoleName.FacilityOwner),
            CancellationToken.None);

        // Assert: counted as an upgrade, with the customer's reason and the
        // customer as the one who moved it.
        context.ChangeTracker.Clear();

        var moved = await context.BookingMoves.AsNoTracking().SingleAsync(row => row.BookingId == booking);
        var stored = await context.BookingUpgradeRequests.AsNoTracking().SingleAsync(row => row.Id == upgradeId);

        using (new AssertionScope())
        {
            approved.Succeeded.Should().BeTrue();
            stored.MoveReason.Should().Be(MoveReason.Weather);
            moved.Kind.Should().Be(MoveKind.Upgrade);
            moved.Reason.Should().Be(MoveReason.Weather);
            moved.ReasonNote.Should().BeNull();
            moved.MovedByUserId.Should().Be(floor.Customer);
        }
    }

    /// <summary>
    /// The moves report counts on the venue's clock, only the venue's own
    /// moves, and shows a move nobody was asked about as not asked.
    /// </summary>
    [Fact]
    public async Task MovesAsync_ShouldCountEachMoveOnTheVenuesDayWithItsReason()
    {
        // Arrange: a move today for the weather, and an old one from before
        // customers were asked, made at half past midnight on Tuesday in
        // Manila — which is still Monday in UTC.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Moves Report Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.Weather));

        context.BookingMoves.Add(new BookingMoveRecord(
            booking,
            new DateTimeOffset(2026, 9, 14, 16, 30, 0, TimeSpan.Zero),
            MoveKind.Free,
            null,
            null,
            null,
            null,
            null));
        await context.SaveChangesAsync();

        // Somebody else's venue, moved the same day. Not this desk's business.
        var elsewhere = await FloorAsync(context, "Moves Report Elsewhere");
        var theirs = await ConfirmedAsync(context, CreateService(context), elsewhere, Wednesday, SevenAm);
        await MovedAsync(
            context,
            CreateService(context),
            theirs,
            elsewhere.Customer,
            new MoveBookingRequest(elsewhere.Pickleball2, Reason: MoveReason.ScheduleChanged));

        var ownerUserId = await OwnerUserIdAsync(context, floor);
        context.ChangeTracker.Clear();

        // Act
        var report = await CreateDeskService(context).MovesAsync(
            ownerUserId,
            new HoursQuery(Today, Today.AddDays(1), HoursGrain.Day),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            report.Succeeded.Should().BeTrue();
            var moves = report.Value!;

            moves.Total.Should().Be(2);
            moves.Periods.Select(period => (period.Starts, period.Free, period.Upgrade)).Should().Equal(
                (Today, 1, 0),
                (Today.AddDays(1), 1, 0));

            // Every reason in each period, zero included, so a chart's lines
            // hold still; the unasked last.
            moves.Periods.First().Reasons.Select(count => count.Reason).Should().Equal(
                [.. MoveReason.All, null]);
            moves.Periods.First().Reasons.Single(count => count.Reason == MoveReason.Weather).Count.Should().Be(1);
            moves.Periods.Last().Reasons.Single(count => count.Reason is null).Count.Should().Be(1);

            // The range's summary leaves out what nobody gave.
            moves.Reasons.Should().BeEquivalentTo(
                [new ReasonCount(MoveReason.Weather, 1), new ReasonCount(null, 1)]);

            // Newest first, on the day the desk would say.
            moves.Moves.Select(move => move.MovedOn).Should().Equal(Today.AddDays(1), Today);
            moves.Moves.Last().Reason.Should().Be(MoveReason.Weather);
            moves.Moves.Last().ToCourtName.Should().NotBeNullOrEmpty();
        }
    }

    /// <summary>
    /// Takings count each payment on the day it was accepted: the booking's on
    /// the day it was confirmed, the upgrade's balance on the day it was
    /// approved, with the platform fee inside the payment set apart — and the
    /// booking's rental is what was paid, not what its hours cost now.
    /// </summary>
    [Fact]
    public async Task TakingsAsync_ShouldCountWhatWasPaidAndWhatTheUpgradeAdded()
    {
        // Arrange: a booking confirmed at five hundred, then upgraded onto a
        // nine-hundred court with the four hundred paid and approved.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Takings Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.DifferentCourt),
            CancellationToken.None);
        await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        var upgradeId = await context.BookingUpgradeRequests
            .Where(row => row.BookingId == booking)
            .Select(row => row.Id)
            .SingleAsync();
        var ownerUserId = await OwnerUserIdAsync(context, floor);
        context.ChangeTracker.Clear();

        var desk = CreateDeskService(context);
        await desk.ApproveUpgradeAsync(
            ownerUserId,
            upgradeId,
            new AuditActor(ownerUserId, UserRoleName.FacilityOwner),
            CancellationToken.None);
        context.ChangeTracker.Clear();

        var paid = await context.Bookings.AsNoTracking()
            .Where(row => row.Id == booking)
            .Select(row => row.PaidTotal)
            .SingleAsync();
        var newCourt = await context.BookableCourts.AsNoTracking()
            .Where(unit => unit.Id == dearer)
            .Select(unit => unit.CourtId)
            .SingleAsync();

        // Act
        var report = await desk.TakingsAsync(
            ownerUserId,
            new HoursQuery(Today, Today, HoursGrain.Day),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            report.Succeeded.Should().BeTrue();
            var day = report.Value!.Periods.Should().ContainSingle().Subject;

            day.Bookings.Should().Be(1);
            day.Hours.Should().Be(1);
            day.Rental.Should().Be(500m);
            day.Upgrades.Should().Be(400m);
            day.UpgradeCount.Should().Be(1);
            day.PlatformFee.Should().BePositive();

            // Everything the customer handed over, and nothing twice.
            (day.Rental + day.Upgrades + day.PlatformFee).Should().Be(paid);

            // Counted to the court it is played on now.
            report.Value.Rows.Should().ContainSingle()
                .Which.CourtId.Should().Be(newCourt);
        }
    }

    /// <summary>
    /// The longer cuts a year-on-year report needs: calendar quarters, halves
    /// and years, clamped to the range, and a range of up to five years.
    /// </summary>
    [Fact]
    public async Task TakingsAsync_ShouldCutByQuarterHalfAndYear()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Takings Grain Courts");
        var ownerUserId = await OwnerUserIdAsync(context, floor);
        var desk = CreateDeskService(context);

        async Task<IEnumerable<(DateOnly, DateOnly)>> Periods(DateOnly from, DateOnly to, string grain) =>
            (await desk.TakingsAsync(ownerUserId, new HoursQuery(from, to, grain), CancellationToken.None))
                .Value!.Periods.Select(period => (period.Starts, period.Ends));

        // Act
        var quarters = await Periods(new(2026, 8, 15), new(2026, 12, 31), HoursGrain.Quarter);
        var halves = await Periods(new(2026, 1, 1), new(2026, 12, 31), HoursGrain.Half);
        var years = await Periods(new(2024, 6, 1), new(2026, 3, 31), HoursGrain.Year);
        var tooWide = await desk.TakingsAsync(
            ownerUserId,
            new HoursQuery(new(2020, 1, 1), new(2026, 1, 1), HoursGrain.Year),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            quarters.Should().Equal(
                (new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 30)),
                (new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)));
            halves.Should().Equal(
                (new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30)),
                (new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31)));
            years.Should().Equal(
                (new DateOnly(2024, 6, 1), new DateOnly(2024, 12, 31)),
                (new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
                (new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));
            tooWide.Failure.Should().Be(DeskFailure.TakingsWindowTooWide);
        }
    }

    [Fact]
    public async Task MovesAsync_ShouldRefuseSomebodyWhoDoesNotWorkTheVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Moves Report Refused Courts");
        var venueId = await context.Courts
            .Where(court => court.Id == floor.CourtId)
            .Select(court => court.FacilityId)
            .SingleAsync();

        // Act: the customer, asking about the venue.
        var report = await CreateDeskService(context).MovesAsync(
            floor.Customer,
            new HoursQuery(Today, Today, HoursGrain.Day, venueId),
            CancellationToken.None);

        // Assert
        report.Failure.Should().Be(DeskFailure.NotAttended);
    }

    [Fact]
    public async Task MoveAsync_ShouldMoveOntoTheHoursItIsGiven()
    {
        // Arrange: a booking at seven, moving to eleven on the same court.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Rescheduled Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var moved = await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(
                floor.Pickleball2,
                [new BookingSlotInput(Wednesday, new TimeOnly(11, 0))], Reason: MoveReason.ScheduleChanged));

        // Assert: a move changes when a booking is as well as where, so the
        // hour it lands on is the one asked for and not the one it came from.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball2);
            stored.Slots.Should().ContainSingle();
            stored.Slots.Single().StartsAt.Should().Be(new TimeOnly(11, 0));
            stored.Slots.Single().Date.Should().Be(Wednesday);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldMoveOntoAnotherDay()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Another Day Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var thursday = Wednesday.AddDays(1);

        // Act
        var moved = await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball1, [new BookingSlotInput(thursday, SevenAm)], Reason: MoveReason.ScheduleChanged));

        // Assert: the dates the booking spans follow the hours it now holds,
        // or the card would go on naming the day it used to be.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeTrue();
            stored.Slots.Single().Date.Should().Be(thursday);
            stored.StartDate.Should().Be(thursday);
            stored.EndDate.Should().Be(thursday);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseMoreHoursThanTheBookingHas()
    {
        // Arrange: one hour booked.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "No Free Hours Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: move it onto two.
        var moved = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(
                floor.Pickleball2,
                [
                    new BookingSlotInput(Wednesday, new TimeOnly(11, 0)),
                    new BookingSlotInput(Wednesday, new TimeOnly(12, 0))
                ], Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: a move changes when and where a booking is, never how much of
        // it there is. Without this a second hour could be had by moving, which
        // is buying one without paying for it.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeFalse();
            stored.Slots.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task QuoteMoveAsync_ShouldSayWhetherTheBookingHasBegun()
    {
        // Arrange: one booking of two hours, asked about from three moments.
        //
        // Two rather than one, because the third moment is half an hour into
        // it. An hour that has begun is being played on the court it was sold
        // on and does not travel — so a booking of a single hour has nothing
        // left to move once it starts, and the quote rightly says so instead
        // of answering the question this test is asking. The eight oclock is
        // what is still ahead at half past seven, and it is what moves.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Today Or Not Courts");
        var sut = CreateService(context);

        var later = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm, hours: 2);

        // Six in the morning on Wednesday at the venue, which is ten at night
        // on Tuesday in UTC. The whole point: the server's day and the venue's
        // are different days at that moment, and the venue's is the one that
        // counts.
        var onTheDay = new DateTimeOffset(
            Tuesday.ToDateTime(new TimeOnly(22, 0)),
            TimeSpan.Zero);

        var thatMorning = CreateService(context, onTheDay);

        // Half past seven at the venue, which is half eleven the night before
        // in UTC. The booking's hour has begun and not yet ended.
        var underWay = CreateService(
            context,
            new DateTimeOffset(Tuesday.ToDateTime(new TimeOnly(23, 30)), TimeSpan.Zero));

        // Act
        var daysAway = await sut.QuoteMoveAsync(
            later,
            floor.Customer,
            floor.Pickleball2,
            null,
            CancellationToken.None);

        var anHourBefore = await thatMorning.QuoteMoveAsync(
            later,
            floor.Customer,
            floor.Pickleball2,
            null,
            CancellationToken.None);

        // Onto its own remaining hour on another court: once it has begun,
        // court is the only thing a move can change.
        var midWay = await underWay.QuoteMoveAsync(
            later,
            floor.Customer,
            floor.Pickleball2,
            null,
            CancellationToken.None);

        // Assert: this is what the move screen reads to decide whether to offer
        // dates at all, and it is asked per request because it turns over while
        // the screen is open.
        using (new AssertionScope())
        {
            daysAway.Value!.IsInPlay.Should().BeFalse();

            // An hour before it starts is inside every notice a venue can set.
            // Not in play — the notice is what closes the door, not the clock
            // having reached it.
            anHourBefore.Failure.Should().Be(BookingFailure.TooLateToMove);

            midWay.Value!.IsInPlay.Should().BeTrue();
        }
    }

    [Fact]
    public async Task QuoteMoveAsync_ShouldLeaveThePlatformFeeOutOfBothSides()
    {
        // Arrange: two courts at the same rate, so a move between them changes
        // nothing about the money.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Same Rate Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var quoted = await sut.QuoteMoveAsync(
            booking,
            floor.Customer,
            floor.Pickleball2,
            null,
            CancellationToken.None);

        // Assert: the platform fee is charged per hour booked and a move buys
        // no hours — the same one ends up somewhere else. Counting it would
        // put a price on a move that costs nothing, and refuse it.
        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            quoted.Succeeded.Should().BeTrue();
            quoted.Value!.BalanceDue.Should().Be(0m);

            // Court rental on both sides, and the fee on neither.
            quoted.Value.RentalNow.Should().Be(stored.Slots.Sum(slot => slot.Amount));
            quoted.Value.RentalNew.Should().Be(quoted.Value.RentalNow);
            stored.Slots.Sum(slot => slot.PlatformFee).Should().BeGreaterThan(0m);
        }
    }

    [Fact]
    public async Task QuoteMoveAsync_ShouldPriceTheUpgradeOnCourtRentalAlone()
    {
        // Arrange: a dearer court, at nine hundred an hour against five.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Rental Only Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var quoted = await sut.QuoteMoveAsync(
            booking,
            floor.Customer,
            dearer,
            null,
            CancellationToken.None);

        // Assert: 900 against the 500 the booking's own hour costs, and the
        // platform fee on neither side of it. Measured against the booking's
        // own rates rather than against what has been settled, so a booking
        // paid in a way this does not know about still quotes honestly.
        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            quoted.Succeeded.Should().BeTrue();
            quoted.Value!.RentalNow.Should().Be(500m);
            quoted.Value.RentalNew.Should().Be(900m);
            quoted.Value.BalanceDue.Should().Be(400m);

            // 400, not 400 plus a fee on 900 less a fee on 500.
            stored.Slots.Sum(slot => slot.PlatformFee).Should().BeGreaterThan(0m);
        }
    }

    [Fact]
    public async Task HistoryAsync_ShouldKeepAnAccountOfWhatHappenedToTheBooking()
    {
        // Arrange: made, paid for, then moved to another court.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Remembered Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Moved five minutes later, on a clock that has actually moved. With a
        // frozen one both entries share a timestamp, and the order they come
        // back in would be the order they were written rather than the order
        // this asks for — which would make the assertion below pass without
        // testing anything.
        var later = CreateService(context, Now.AddMinutes(5));

        await MovedAsync(
            context,
            later,
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            approvedAt: Now.AddMinutes(10));

        // Act
        var history = await sut.HistoryAsync(booking, floor.Customer, CancellationToken.None);

        // Assert: a booking says where it ended up and never how it got there.
        // Without this the move that just happened leaves no trace at all.
        using (new AssertionScope())
        {
            history.Succeeded.Should().BeTrue();

            var entries = history.Value!.ToArray();
            entries.Select(entry => entry.Action).Should().Contain(AuditAction.BookingCreated);
            entries.Select(entry => entry.Action).Should().Contain(AuditAction.BookingMoveRequested);

            // Newest first: what just happened is what somebody opens a
            // history for, so it goes at the top rather than at the bottom of
            // everything they already knew.
            entries.Should().BeInDescendingOrder(entry => entry.At);
            entries[0].Action.Should().Be(AuditAction.BookingMoveApproved);

            // And the move says where it came from, which the booking itself
            // can no longer answer.
            var moved = entries.Single(entry => entry.Action == AuditAction.BookingMoveApproved);
            moved.Description.Should().Contain("Moved from");
            moved.Description.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task HistoryAsync_ShouldSayWhenAHoldRanOut()
    {
        // Arrange: a booking made and never paid for, read back long after its
        // hold would have ended.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Lapsed History Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act: asked a day later, by which time the hold is long gone.
        var later = CreateService(context, Now.AddDays(1));
        var history = await later.HistoryAsync(
            created.Value!.Id,
            floor.Customer,
            CancellationToken.None);

        // Assert: nothing writes this entry, because nothing notices a hold
        // ending — a booking does not change, it simply stops holding. Without
        // it the history says the booking was made and then stops, at the exact
        // moment the reader wants to know what became of it.
        using (new AssertionScope())
        {
            var entries = history.Value!.ToArray();

            entries.Select(entry => entry.Action).Should().Contain(AuditAction.BookingHoldExpired);

            // Newest first, and the expiry is the newest thing that happened.
            entries[0].Action.Should().Be(AuditAction.BookingHoldExpired);
            entries[0].Description.Should().Contain("back on sale");

            // Timed at the moment the hold ended, not at the moment somebody
            // happened to open the page.
            entries[0].At.Should().BeCloseTo(Now.AddMinutes(PaymentHold.DefaultMinutes), TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public async Task HistoryAsync_ShouldNotSayAHoldRanOutWhenTheReceiptIsIn()
    {
        // Arrange: paid for, so the clock stopped mattering.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Paid History Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: read a day later, well past when an unpaid hold would have gone.
        var later = CreateService(context, Now.AddDays(1));
        var history = await later.HistoryAsync(booking, floor.Customer, CancellationToken.None);

        // Assert: somebody who paid did not lose their court to a clock, and
        // their history must not tell them they did.
        history.Value!.Select(entry => entry.Action)
            .Should().NotContain(AuditAction.BookingHoldExpired);
    }

    [Fact]
    public async Task HistoryAsync_ShouldNotShowOneCustomerAnothersBooking()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Private History Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: somebody else's sign-in against this booking's id.
        var history = await sut.HistoryAsync(booking, Guid.NewGuid(), CancellationToken.None);

        // Assert: the same answer as a booking that is not there, so an id
        // cannot be probed for whose it is.
        using (new AssertionScope())
        {
            history.Succeeded.Should().BeFalse();
            history.Failure.Should().Be(BookingFailure.CourtNotFound);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseABookingTheVenueHasNotConfirmed()
    {
        // Arrange: paid for and handed over, but nobody at the venue has looked
        // at the receipt yet.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Unconfirmed Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        var waiting = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        waiting.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            Now);
        waiting.SubmitForVerification(Now);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        // Act
        var asked = await sut.MoveAsync(
            waiting.Id,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: this one might still be turned down. Moving it takes hours
        // off one court and puts them on another for an agreement that may
        // never stand, and the hours it left are back on sale in the meantime.
        var card = await sut.GetAsync(waiting.Id, floor.Customer, CancellationToken.None);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.NotMovable);

            // And the screen is told, so the link is never offered.
            card.Value!.CanBeMoved.Should().BeFalse();
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseTheCourtAndHoursItAlreadyHas()
    {
        // Arrange: a booking, asked to move to exactly where it is.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Unchanged Move Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: its own court, and no hours named, which keeps its own.
        var moved = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball1, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: refused, and crucially the venue's limit is untouched. This
        // used to succeed — the booking was rewritten with what it already had
        // and charged a move for it, so three presses of a button that did
        // nothing left a customer unable to move at all.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeFalse();
            moved.Failure.Should().Be(BookingFailure.NothingWouldChange);
            stored.MoveCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldAllowTheSameCourtAtDifferentHours()
    {
        // Arrange: the court is fine, the time is not — which is the move that
        // had no way of being asked for at all until the court list started
        // offering the booking's own court.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Same Court New Hours");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var moved = await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(
                floor.Pickleball1,
                [new BookingSlotInput(Wednesday, new TimeOnly(14, 0))], Reason: MoveReason.ScheduleChanged));

        // Assert
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            moved.Succeeded.Should().BeTrue();
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.Slots.Should().ContainSingle(slot => slot.StartsAt == new TimeOnly(14, 0));
            stored.MoveCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldStopAtTheVenuesLimit()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Counted Move Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var courts = new[] { floor.Pickleball2, floor.Pickleball3, floor.Pickleball1 };

        foreach (var court in courts)
        {
            (await MovedAsync(context, sut, booking, floor.Customer, new MoveBookingRequest(court, Reason: MoveReason.ScheduleChanged)))
                .Succeeded.Should().BeTrue();
        }

        // Act: a fourth, against a limit of three.
        var again = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: the limit is the venue's own dial, and it is the only thing
        // standing between a court and somebody moving around it all afternoon.
        using (new AssertionScope())
        {
            again.Succeeded.Should().BeFalse();
            again.Failure.Should().Be(BookingFailure.MoveLimitReached);
        }
    }

    [Fact]
    public async Task MoveAsync_ShouldRefuseACourtThatCostsMoreThanWasPaid()
    {
        // Arrange: a second pickleball court in the same building, at nine
        // hundred an hour against the five already paid.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.MoveAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: nothing on the free path collects money. Letting it through
        // would hand the customer a better court and hand the venue the bill;
        // dearer hours are an upgrade, paid for before the venue is asked.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.MoveCostsMore);
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);
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

        var paidBefore = await context.Bookings
            .AsNoTracking()
            .Where(row => row.Id == booking)
            .Select(row => row.PaidTotal)
            .SingleAsync();

        // Act
        await MovedAsync(
            context,
            sut,
            booking,
            floor.Customer,
            new MoveBookingRequest(cheaper, Reason: MoveReason.ScheduleChanged));

        // Assert: it moves, and nothing is given back. There are no refunds —
        // the customer keeps the booking and pays no more, which is what the
        // booking policy says and what the move screen repeats.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            stored.BookableCourtId.Should().Be(cheaper);
            stored.PaidTotal.Should().Be(paidBefore);
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
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
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
            new MoveBookingRequest(floor.Pickleball2, Reason: MoveReason.ScheduleChanged),
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
    [Fact]
    public async Task RequestUpgradeAsync_ShouldWriteItDownAndLeaveTheBookingWhereItIs()
    {
        // Arrange: a second pickleball court at nine hundred an hour, against
        // the five hundred already paid.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Asked Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: the request stands on its own and the booking has not budged.
        // A venue that has not seen the money must not have given up its court,
        // and a customer who has not paid must not have lost theirs.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == booking);

        var upgrade = await context.BookingUpgradeRequests
            .AsNoTracking()
            .Include(row => row.Slots)
            .SingleAsync(row => row.BookingId == booking);

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeTrue();
            asked.Value!.BalanceDue.Should().Be(400m);
            asked.Value.Status.Should().Be(UpgradeStatus.AwaitingPayment);
            asked.Value.Slots.Should().ContainSingle();

            upgrade.ToBookableCourtId.Should().Be(dearer);
            upgrade.RentalNow.Should().Be(500m);
            upgrade.RentalNew.Should().Be(900m);
            upgrade.BalanceDue.Should().Be(400m);
            upgrade.Slots.Should().ContainSingle(slot => slot.StartsAt == SevenAm);

            // The booking itself: untouched, and no move counted. Nothing has
            // happened to it yet but a piece of paper.
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task RequestUpgradeAsync_ShouldHoldTheHoursOnAClock()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Hold Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: hours nobody has paid for cannot be held for ever, so the
        // request carries the venue's own hold, timed from the server's clock.
        using (new AssertionScope())
        {
            asked.Value!.HoldsUntil.Should().BeAfter(Now);
            asked.Value.HasLapsed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task RequestUpgradeAsync_ShouldRefuseHoursThatCostTheSameOrLess()
    {
        // Arrange: a cheaper court. Moving onto it is free and immediate, so
        // there is nothing for a checkout to collect.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Cheaper Courts");
        var sut = CreateService(context);
        var cheaper = await SecondCourtAsync(context, floor, pickleballRate: 300m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(cheaper, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: refused, and nothing written down. Sending somebody to pay
        // nought pesos is a step whose only effect is to make them wonder what
        // they are being charged for.
        context.ChangeTracker.Clear();

        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.NothingToUpgrade);
            (await context.BookingUpgradeRequests.AsNoTracking().AnyAsync(row => row.BookingId == booking))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task RequestUpgradeAsync_ShouldRefuseASecondOneWhileTheFirstIsOpen()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Twice Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Act
        var again = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: one at a time. Two open requests and the customer can be
        // paying for hours while the venue is approving different ones.
        context.ChangeTracker.Clear();

        using (new AssertionScope())
        {
            again.Succeeded.Should().BeFalse();
            again.Failure.Should().Be(BookingFailure.MoveAlreadyRequested);
            (await context.BookingUpgradeRequests.AsNoTracking().CountAsync(row => row.BookingId == booking))
                .Should().Be(1);
        }
    }

    [Fact]
    public async Task RequestUpgradeAsync_ShouldRefuseABookingTheVenueHasNotConfirmed()
    {
        // Arrange: a booking still waiting to be paid for. Only a booking the
        // venue has confirmed can move at all, and paying does not buy a way
        // around that.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Unconfirmed Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);

        var created = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var asked = await sut.RequestUpgradeAsync(
            created.Value!.Id,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.NotMovable);
        }
    }

    [Fact]
    public async Task RequestUpgradeAsync_ShouldCountAgainstTheVenuesMoveLimit()
    {
        // Arrange: a booking that has spent every move the venue allows.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Limit Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Three free moves, which is what this venue allows.
        foreach (var court in new[] { floor.Pickleball2, floor.Pickleball3, floor.Pickleball1 })
        {
            (await MovedAsync(context, sut, booking, floor.Customer, new MoveBookingRequest(court, Reason: MoveReason.ScheduleChanged)))
                .Succeeded.Should().BeTrue();
        }

        // Act
        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Assert: an upgrade is still a move. Paying for one must not be a way
        // around the dial the venue set.
        using (new AssertionScope())
        {
            asked.Succeeded.Should().BeFalse();
            asked.Failure.Should().Be(BookingFailure.MoveLimitReached);
        }
    }

    [Fact]
    public async Task OpenUpgradeAsync_ShouldAnswerNothingWhenNoneIsWaiting()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade None Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var open = await sut.OpenUpgradeAsync(booking, floor.Customer, CancellationToken.None);

        // Assert: nothing waiting is an ordinary answer, not a not-found. The
        // question the upgrade screen asks is what is open, and "nothing" is a
        // complete reply to it.
        using (new AssertionScope())
        {
            open.Succeeded.Should().BeTrue();
            open.Value.Should().BeNull();
        }
    }

    [Fact]
    public async Task OpenUpgradeAsync_ShouldReadBackTheOneThatIsWaiting()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Read Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Act
        var open = await sut.OpenUpgradeAsync(booking, floor.Customer, CancellationToken.None);

        // Assert: this is what a refresh reads, so it has to carry enough to
        // rebuild the page the customer left.
        using (new AssertionScope())
        {
            open.Value.Should().NotBeNull();
            open.Value!.Id.Should().Be(asked.Value!.Id);
            open.Value.BalanceDue.Should().Be(400m);
            open.Value.Slots.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task OpenUpgradeAsync_ShouldAnswerNothingForAnotherCustomersBooking()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Stranger Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var open = await sut.OpenUpgradeAsync(booking, Guid.NewGuid(), CancellationToken.None);

        // Assert: another customer's booking answers the same as one that is
        // not there, so an id cannot be probed for whether it belongs to
        // somebody.
        using (new AssertionScope())
        {
            open.Succeeded.Should().BeFalse();
            open.Failure.Should().Be(BookingFailure.CourtNotFound);
        }
    }

    [Fact]
    public async Task AttachUpgradeReceiptAsync_ShouldStopTheClock()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Receipt Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var asked = await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Act
        var attached = await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        // Assert: the receipt is in, and from here the hold stops mattering.
        // Somebody who has paid must not lose their hours to a queue they are
        // not in — which is exactly what a booking's own receipt does.
        context.ChangeTracker.Clear();

        var stored = await context.BookingUpgradeRequests
            .AsNoTracking()
            .SingleAsync(row => row.Id == asked.Value!.Id);

        using (new AssertionScope())
        {
            attached.Succeeded.Should().BeTrue();
            attached.Value!.ReceiptUrl.Should().Be(Receipt);
            stored.ReceiptUrl.Should().Be(Receipt);
            stored.ReceiptUploadedAt.Should().NotBeNull();

            // Uploading IS sending. It was not, and the gap between the two
            // left an upgrade paid for and holding its hours where no desk
            // could see it.
            stored.Status.Should().Be(UpgradeStatus.AwaitingApproval);

            // And the hold stops mattering: somebody who has paid must not
            // lose their hours to a queue they are not in.
            stored.HoldsTheCourtAt(stored.HoldsUntil.AddHours(1)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AttachUpgradeReceiptAsync_ShouldRefuseALinkOffTheseCloudinaryAccount()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Untrusted Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Act
        var attached = await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest("https://example.com/image/upload/v1/anything.jpg"),
            CancellationToken.None);

        // Assert: the browser reports where it put the file, so the link is the
        // customer's word for it. A venue must never be shown a picture nobody
        // here can vouch for.
        using (new AssertionScope())
        {
            attached.Succeeded.Should().BeFalse();
            attached.Failure.Should().Be(BookingFailure.UntrustedReceiptUrl);
        }
    }

    [Fact]
    public async Task AttachUpgradeReceiptAsync_ShouldHandItToTheVenueAndLeaveTheBookingWhereItIs()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Submit Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        // Act
        var sent = await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        // Assert: it is the venue's turn, and the booking has still not moved.
        // Sending money is not the same as being given the court.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            sent.Succeeded.Should().BeTrue();
            sent.Value!.Status.Should().Be(UpgradeStatus.AwaitingApproval);
            stored.BookableCourtId.Should().Be(floor.Pickleball1);
            stored.MoveCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task AttachUpgradeReceiptAsync_ShouldRefuseAnUpgradeThatWasNeverAskedFor()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade No Receipt Courts");
        var sut = CreateService(context);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: no upgrade was ever asked for on this booking.
        var sent = await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        // Assert: there is nothing to attach it to. A receipt on its own is
        // not a request to upgrade anything.
        using (new AssertionScope())
        {
            sent.Succeeded.Should().BeFalse();
            sent.Failure.Should().Be(BookingFailure.MoveRequestNotFound);
        }
    }

    [Fact]
    public async Task AttachUpgradeReceiptAsync_ShouldTakeAReplacementWhileTheVenueIsLooking()
    {
        // Arrange
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Resend Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);
        var dearer = await SecondCourtAsync(context, floor, pickleballRate: 900m);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        await sut.RequestUpgradeAsync(
            booking,
            floor.Customer,
            new MoveBookingRequest(dearer, Reason: MoveReason.ScheduleChanged),
            CancellationToken.None);

        await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        const string better =
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/clearer-receipt.jpg";

        // Act
        var again = await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(better),
            CancellationToken.None);

        // Assert: sending the wrong picture is the one mistake worth being
        // able to undo, and without this the only way out is to ring the
        // venue. It stays with them either way.
        using (new AssertionScope())
        {
            again.Succeeded.Should().BeTrue();
            again.Value!.ReceiptUrl.Should().Be(better);
            again.Value.Status.Should().Be(UpgradeStatus.AwaitingApproval);
        }
    }

    [Fact]
    public async Task AttachUpgradeReceiptAsync_ShouldSayWhenNoUpgradeIsWaiting()
    {
        // Arrange: a booking nobody has asked to upgrade.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Upgrade Missing Courts");
        var sut = CreateService(context);
        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var attached = await sut.AttachUpgradeReceiptAsync(
            booking,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            attached.Succeeded.Should().BeFalse();
            attached.Failure.Should().Be(BookingFailure.MoveRequestNotFound);
        }
    }

    [Fact]
    public async Task AttachReceiptAsync_ShouldRefuseAVenueWithNoWayOfBeingPaid()
    {
        // Arrange: a venue that has set up neither a GCash number nor a QR
        // code, which is how every venue starts.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Unpayable Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var attached = await sut.AttachReceiptAsync(
            created.Value!.Id,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        // Assert: refused, and the booking is left waiting to be paid rather
        // than handed to a desk with no account to check it against. Taking the
        // receipt would also have stopped the hold's clock, so the court would
        // sit held for ever on the strength of a payment nobody could receive.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == created.Value.Id);

        using (new AssertionScope())
        {
            attached.Succeeded.Should().BeFalse();
            attached.Failure.Should().Be(BookingFailure.VenueCannotBePaid);
            stored.ReceiptUrl.Should().BeNull();
            stored.Status.Should().Be(BookingStatus.PendingPayment);
        }
    }

    [Fact]
    public async Task AttachReceiptAsync_ShouldSendItOnceTheVenueCanBePaid()
    {
        // Arrange: the same venue, now with a GCash number.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Payable Courts");
        var sut = CreateService(context);
        await PayableAsync(context, floor);

        var created = await sut.CreateAsync(
            Hourly(floor.Pickleball1, Wednesday, SevenAm),
            floor.Customer,
            CancellationToken.None);

        // Act
        var attached = await sut.AttachReceiptAsync(
            created.Value!.Id,
            floor.Customer,
            new AttachReceiptRequest(Receipt),
            CancellationToken.None);

        // Assert: taken, and handed straight to the venue. Sending the receipt
        // IS the submission — there is no step in between for a booking to get
        // stuck in, holding its court where no desk can see it.
        context.ChangeTracker.Clear();

        var stored = await context.Bookings
            .AsNoTracking()
            .SingleAsync(row => row.Id == created.Value.Id);

        using (new AssertionScope())
        {
            attached.Succeeded.Should().BeTrue();
            stored.ReceiptUrl.Should().Be(Receipt);
            stored.Status.Should().Be(BookingStatus.PendingVerification);
        }
    }

    /// <summary>
    /// Gives the venue a GCash number.
    ///
    /// A receipt is evidence of a payment, and a venue with no account has none
    /// to have been paid into — so anything that sends one has to set this up
    /// first, exactly as a real venue would before taking a booking.
    /// </summary>
    private static async Task PayableAsync(AppDbContext context, Floor floor)
    {
        var ownerId = await context.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.Id == floor.Pickleball1)
            .Select(unit => unit.Court.Facility.FacilityOwnerId)
            .SingleAsync();

        var owner = await context.FacilityOwners.SingleAsync(row => row.Id == ownerId);

        owner.SetPaymentDetails(
            "0917 555 0101",
            "Demo Sports Center",
            null,
            owner.PartialBookingExpiryMinutes,
            Now);

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

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
        TimeOnly hour,
        int hours = 1)
    {
        var created = await bookings.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.Hourly,
                [
                    .. Enumerable
                        .Range(0, hours)
                        .Select(step => new BookingSlotInput(date, hour.AddHours(step)))
                ]),
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

    [Fact]
    public async Task MoveWindowAsync_ShouldOfferTheBuildingsHoursBeforeACourtIsChosen()
    {
        // Arrange: the move screen asks for a date before it asks for a court,
        // so the hours on offer cannot be any court's.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Hours First Courts");
        var sut = CreateService(context);

        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act
        var window = await sut.MoveWindowAsync(
            booking,
            floor.Customer,
            Wednesday,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            window.Succeeded.Should().BeTrue();
            window.Value!.IsClosed.Should().BeFalse();

            // Six in the morning to ten at night, in hours. The venue's, not
            // one floor's.
            window.Value.Slots.Should().HaveCount(16);
            window.Value.Slots.First().StartsAt.Should().Be(new TimeOnly(6, 0));
            window.Value.Slots.Last().EndsAt.Should().Be(new TimeOnly(22, 0));

            // One hour booked is one hour to place. A move changes when and
            // where a booking is, never how much of it there is.
            window.Value.SlotsNeeded.Should().Be(1);
            window.Value.SlotLengthMinutes.Should().Be(60);

            // A day two days out has none of it behind us.
            window.Value.Slots.Should().OnlyContain(slot => !slot.HasPassed);
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldOfferEveryCourtOfTheSameSportPriced()
    {
        // Arrange: pickleball runs three across on this floor, so a booking on
        // one division has two others to go to — and its own, at another hour.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Three Across Courts");
        var sut = CreateService(context);

        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        // Act: the same day, an hour later.
        var options = await sut.MoveOptionsAsync(
            booking,
            floor.Customer,
            new MoveOptionsRequest(Slots: [new BookingSlotInput(Wednesday, EightAm)]),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeTrue();

            // All three pickleball divisions, its own included: keeping the
            // court and changing the hour is a move too. Basketball and
            // volleyball are the same floor and the wrong sport, and a booking
            // carries the sport it was sold as.
            options.Value!.Courts.Should().HaveCount(3);
            options.Value.Courts.Select(court => court.BookableCourtId)
                .Should().BeEquivalentTo([floor.Pickleball1, floor.Pickleball2, floor.Pickleball3]);

            options.Value.Courts.Should().ContainSingle(court => court.IsCurrentCourt);

            // Nothing has started, so the whole booking is on the move.
            options.Value.IsInPlay.Should().BeFalse();
            options.Value.HoursStaying.Should().Be(0);
            options.Value.HoursMoving.Should().Be(1);

            // Seven in the morning and eight in the morning are both standard
            // on this rate card, so every one of these is a free move.
            options.Value.Courts.Should().OnlyContain(court => court.BalanceDue == 0m);
            options.Value.Courts.Should().OnlyContain(court => !court.IsUpgrade);
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldLeaveOutAnHourSomebodyElseHolds()
    {
        // Arrange: a booking at seven, and somebody else already on the
        // division next door at the hour it wants.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Already Taken Courts");
        var sut = CreateService(context);

        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var neighbour = await sut.CreateAsync(
            Hourly(floor.Pickleball2, Wednesday, EightAm),
            Guid.NewGuid(),
            CancellationToken.None);

        neighbour.Succeeded.Should().BeTrue();

        // Act
        var options = await sut.MoveOptionsAsync(
            booking,
            floor.Customer,
            new MoveOptionsRequest(Slots: [new BookingSlotInput(Wednesday, EightAm)]),
            CancellationToken.None);

        // Assert: the court that cannot have that hour is not offered. A card
        // that ends in a refusal is a question asked twice.
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeTrue();
            options.Value!.Courts.Select(court => court.BookableCourtId)
                .Should().NotContain(floor.Pickleball2);
            options.Value.Courts.Should().HaveCount(2);
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldLeaveOutACourtClosedForWork()
    {
        // Arrange: the whole floor goes under maintenance, which takes every
        // division on it with it.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Under Repair Courts");
        var sut = CreateService(context);

        var booking = await ConfirmedAsync(context, sut, floor, Wednesday, SevenAm);

        var facilityId = await context.Courts
            .Where(court => court.Id == floor.CourtId)
            .Select(court => court.FacilityId)
            .SingleAsync();

        context.MaintenancePeriods.Add(new MaintenancePeriod(
            facilityId,
            floor.CourtId,
            new DateTimeOffset(Wednesday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(Thursday.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            "Resurfacing",
            Guid.NewGuid(),
            Now));

        await context.SaveChangesAsync();

        // Act
        var options = await sut.MoveOptionsAsync(
            booking,
            floor.Customer,
            new MoveOptionsRequest(Slots: [new BookingSlotInput(Wednesday, EightAm)]),
            CancellationToken.None);

        // Assert: nowhere to go, said as an empty list rather than as a grid of
        // courts that cannot be picked.
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeTrue();
            options.Value!.Courts.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldRefuseADayBookingOnceItsDayHasBegun()
    {
        // Arrange: a whole day on Tuesday, asked about from Tuesday afternoon.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Day Already Started Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            new CreateBookingRequest(floor.Pickleball1, BookingKind.WholeDay, [.. AllHours(Tuesday)]),
            floor.Customer,
            CancellationToken.None);

        created.Succeeded.Should().BeTrue();

        var booking = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        booking.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            Now);
        booking.SubmitForVerification(Now);
        booking.Confirm(Now);
        await context.SaveChangesAsync();

        // Noon on Tuesday at the venue, which is four in the morning in UTC.
        var midday = CreateService(
            context,
            new DateTimeOffset(Tuesday.ToDateTime(new TimeOnly(4, 0)), TimeSpan.Zero));

        // Act
        var options = await midday.MoveOptionsAsync(
            created.Value!.Id,
            floor.Customer,
            new MoveOptionsRequest(Dates: [Thursday]),
            CancellationToken.None);

        // Assert: half a day on one court and half on another is not the thing
        // that was bought, so the day it is on is the day it stays on.
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeFalse();
            options.Failure.Should().Be(BookingFailure.DayBookingInPlay);
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldCarryARunOfDaysToTheDatesItIsGiven()
    {
        // Arrange: two days, Wednesday and Thursday, moved to two others.
        //
        // The two it is given do not run back to back. The picker names each
        // date on its own and lets it be unchosen again, so a gap is a thing a
        // customer can ask for — and a move is not a sale: the number of days
        // cannot change and nothing is being bought that was not paid for.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Run Of Days Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Wednesday), .. AllHours(Thursday)]),
            floor.Customer,
            CancellationToken.None);

        created.Succeeded.Should().BeTrue();

        var booking = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        booking.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            Now);
        booking.SubmitForVerification(Now);
        booking.Confirm(Now);
        await context.SaveChangesAsync();

        // Act: Friday and Monday, with the weekend left out between them.
        var options = await sut.MoveOptionsAsync(
            created.Value!.Id,
            floor.Customer,
            new MoveOptionsRequest(Dates: [Friday, Monday]),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeTrue();
            options.Value!.Courts.Should().NotBeEmpty();

            // Two days asked for, two days priced — on every court offered.
            options.Value.Courts.Should().OnlyContain(court =>
                court.Slots.Select(slot => slot.Date).Distinct().Count() == 2);
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldRefuseFewerDatesThanTheBookingHas()
    {
        // Arrange: a run of two days, asked to land on one.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Wrong Count Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            new CreateBookingRequest(
                floor.Pickleball1,
                BookingKind.MultiDay,
                [.. AllHours(Wednesday), .. AllHours(Thursday)]),
            floor.Customer,
            CancellationToken.None);

        created.Succeeded.Should().BeTrue();

        var booking = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        booking.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            Now);
        booking.SubmitForVerification(Now);
        booking.Confirm(Now);
        await context.SaveChangesAsync();

        // Act
        var options = await sut.MoveOptionsAsync(
            created.Value!.Id,
            floor.Customer,
            new MoveOptionsRequest(Dates: [Friday]),
            CancellationToken.None);

        // Assert: said as a bad request rather than as an empty list. Left to
        // the search it would fail every court in turn and come back with
        // nothing, which reads as a full venue.
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeFalse();
            options.Failure.Should().Be(BookingFailure.KindDoesNotMatchSlots);
        }
    }

    [Fact]
    public async Task MoveOptionsAsync_ShouldCarryAWholeDayToAnotherDate()
    {
        // Arrange: a whole day on Thursday, moved to Friday before it starts.
        await using var context = database.CreateContext();
        var floor = await FloorAsync(context, "Whole Day Move Courts");
        var sut = CreateService(context);

        var created = await sut.CreateAsync(
            new CreateBookingRequest(floor.Pickleball1, BookingKind.WholeDay, [.. AllHours(Thursday)]),
            floor.Customer,
            CancellationToken.None);

        created.Succeeded.Should().BeTrue();

        var booking = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        booking.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            Now);
        booking.SubmitForVerification(Now);
        booking.Confirm(Now);
        await context.SaveChangesAsync();

        // Act: a date and no hours. A day's hours are whatever the court is
        // open for, so they cannot be named until a court is.
        var options = await sut.MoveOptionsAsync(
            created.Value!.Id,
            floor.Customer,
            new MoveOptionsRequest(Dates: [Friday]),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeTrue();

            // All three divisions, its own included: the date changes, so this
            // is a move even where the court does not.
            options.Value!.Courts.Should().HaveCount(3);

            // Thursday and Friday are both weekdays on this rate card, so the
            // day costs what it cost and nobody is asked for anything.
            options.Value.Courts.Should().OnlyContain(court => court.BalanceDue == 0m);
        }
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

        // The shortest notice a venue can set. These tests book days just
        // ahead of the clock, and the notice has tests of its own.
        owner.SetMoveNotice(BookingMove.SmallestNoticeDays, Now);
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

    /// <param name="now">
    /// For a test that has to read the booking at a different moment than it was
    /// made — whether a game is under way is a question about the clock.
    /// </param>
    private static BookingService CreateService(
        AppDbContext context,
        DateTimeOffset? now = null) => new(
        context,
        Assets(),
        new SilentNotifier(),
        new AuditLogger(context, new FixedTimeProvider(now ?? Now)),
        new FixedTimeProvider(now ?? Now),
        NullLogger<BookingService>.Instance);

    /// <summary>The account that owns the floor's venue, which is who reads its desk.</summary>
    private static async Task<Guid> OwnerUserIdAsync(AppDbContext context, Floor floor) =>
        await context.BookableCourts
            .Where(unit => unit.Id == floor.Pickleball1)
            .Select(unit => unit.Court.Facility.FacilityOwner.UserId)
            .SingleAsync();

    private static DeskService CreateDeskService(AppDbContext context, DateTimeOffset? now = null) => new(
        context,
        new SilentNotifier(),
        new AuditLogger(context, new FixedTimeProvider(now ?? Now)),
        new FixedTimeProvider(now ?? Now),
        NullLogger<DeskService>.Instance);

    /// <summary>
    /// Asks for a free move and has the venue agree to it, which is how a move
    /// happens now. Answers with the refusal when the asking is refused, so a
    /// test can still see which rule said no.
    /// </summary>
    private static async Task<BookingResult<BookingDetail>> MovedAsync(
        AppDbContext context,
        BookingService bookings,
        Guid booking,
        Guid customer,
        MoveBookingRequest request,
        DateTimeOffset? approvedAt = null)
    {
        var asked = await bookings.MoveAsync(booking, customer, request, CancellationToken.None);

        if (!asked.Succeeded)
        {
            return asked;
        }

        var waiting = await context.BookingUpgradeRequests
            .Where(row => row.BookingId == booking && row.Status == UpgradeStatus.AwaitingApproval)
            .Select(row => new
            {
                row.Id,
                OwnerUserId = row.Booking.BookableCourt.Court.Facility.FacilityOwner.UserId
            })
            .SingleAsync();

        context.ChangeTracker.Clear();

        var approved = await CreateDeskService(context, approvedAt).ApproveUpgradeAsync(
            waiting.OwnerUserId,
            waiting.Id,
            new AuditActor(waiting.OwnerUserId, UserRoleName.FacilityOwner),
            CancellationToken.None);

        approved.Succeeded.Should().BeTrue();
        context.ChangeTracker.Clear();

        return await bookings.GetAsync(booking, customer, CancellationToken.None);
    }

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
    /// <summary>Keeps every letter handed to it, so a test can read what would have gone out.</summary>
    private sealed class CapturingEmailSender : ITransactionalEmailSender
    {
        public List<TransactionalEmailMessage> Sent { get; } = [];

        public Task SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
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
