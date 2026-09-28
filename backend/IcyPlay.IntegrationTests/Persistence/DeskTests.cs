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

/// <summary>
/// The venue's desk: the queue of payments waiting to be checked, and the two
/// answers somebody standing at it can give.
///
/// What these are really about is who may see and decide what. A desk that
/// showed one venue's bookings to another venue's staff would be worse than one
/// that showed nothing.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DeskTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Tuesday = new(2026, 9, 15);

    /// <summary>The venue's today. 09:00 UTC is 5pm in Manila.</summary>
    private static readonly DateOnly Today = new(2026, 9, 14);
    private static readonly TimeOnly SevenAm = new(7, 0);
    private static readonly TimeOnly EightAm = new(8, 0);

    [Fact]
    public async Task ListAsync_ShouldShowTheOwnerWhatIsWaitingOnTheirVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Waiting Desk Courts");
        await SubmittedAsync(context, venue, SevenAm);
        await SubmittedAsync(context, venue, EightAm);
        var sut = CreateService(context);

        // Act
        var waiting = await sut.ListAsync(venue.OwnerUserId, new DeskQuery(), CancellationToken.None);

        // Assert
        var first = waiting.Value!.Items.First();

        using (new AssertionScope())
        {
            waiting.Value!.TotalItems.Should().Be(2);
            first.Status.Should().Be(nameof(BookingStatus.PendingVerification));
            // The desk checks a GCash receipt against who sent it, so the name
            // has to be on the card.
            first.CustomerName.Should().Be("Booking Bianca");
            first.ReceiptUrl.Should().NotBeNull();
            // Hour by hour, so the total can be read back rather than trusted.
            first.Slots.Should().ContainSingle();
            first.Total.Should().Be(first.RentalTotal + first.PlatformFeeTotal);
        }
    }

    [Fact]
    public async Task ListAsync_ShouldShowAnAttendantTheVenueTheyAreOnAndNoOther()
    {
        // Arrange: two venues, and somebody on the desk of the first.
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "Attended Desk Courts");
        var theirs = await VenueAsync(context, "Other Desk Courts");
        await SubmittedAsync(context, mine, SevenAm);
        await SubmittedAsync(context, theirs, SevenAm);

        var attendant = await AttendantAsync(context, mine.FacilityId);
        var sut = CreateService(context);

        // Act
        var waiting = await sut.ListAsync(attendant, new DeskQuery(), CancellationToken.None);

        // Assert: one venue's queue, not the platform's.
        using (new AssertionScope())
        {
            waiting.Value!.TotalItems.Should().Be(1);
            waiting.Value!.Items.Single().FacilityId.Should().Be(mine.FacilityId);
        }
    }

    [Fact]
    public async Task ListAsync_ShouldRefuseAVenueTheCallerDoesNotWork()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "Filter Desk Courts");
        var theirs = await VenueAsync(context, "Not Mine Desk Courts");
        var sut = CreateService(context);

        // Act: my sign-in, their facility id.
        var result = await sut.ListAsync(
            mine.OwnerUserId,
            new DeskQuery(FacilityId: theirs.FacilityId),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Succeeded.Should().BeFalse();
            result.Failure.Should().Be(DeskFailure.NotAttended);
        }
    }

    [Fact]
    public async Task ConfirmAsync_ShouldBookTheCourtAndTellTheCustomer()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Confirming Desk Courts");
        var booking = await SubmittedAsync(context, venue, SevenAm);
        var letters = new RecordingNotifier();
        var sut = CreateService(context, letters);

        // Act
        var result = await sut.ConfirmAsync(
            venue.OwnerUserId,
            booking,
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Assert
        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);
        var trail = await context.AuditLogs
            .AsNoTracking()
            .CountAsync(entry => entry.Action == AuditAction.BookingConfirmed && entry.EntityId == booking);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            result.Value!.Status.Should().Be(nameof(BookingStatus.Confirmed));
            stored.Status.Should().Be(BookingStatus.Confirmed);
            stored.ConfirmedAt.Should().Be(Now);
            // The one letter that reads as a confirmation, sent when a person
            // has actually looked at the payment.
            letters.Confirmed.Should().ContainSingle().Which.Should().Be(booking);
            trail.Should().Be(1);
        }
    }

    [Fact]
    public async Task ConfirmAsync_ShouldRefuseASecondPressOnTheSameBooking()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Twice Confirmed Courts");
        var booking = await SubmittedAsync(context, venue, SevenAm);
        var letters = new RecordingNotifier();
        var sut = CreateService(context, letters);

        await sut.ConfirmAsync(venue.OwnerUserId, booking, Desk(venue.OwnerUserId), CancellationToken.None);

        // Act: two people at one desk, both pressing.
        var again = await sut.ConfirmAsync(
            venue.OwnerUserId,
            booking,
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Assert: the first press stands, and the customer is thanked once.
        using (new AssertionScope())
        {
            again.Succeeded.Should().BeFalse();
            again.Failure.Should().Be(DeskFailure.NotWaiting);
            letters.Confirmed.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ConfirmAsync_ShouldRefuseABookingAtSomebodyElsesVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "My Desk Courts");
        var theirs = await VenueAsync(context, "Their Desk Courts");
        var booking = await SubmittedAsync(context, theirs, SevenAm);
        var sut = CreateService(context);

        // Act
        var result = await sut.ConfirmAsync(
            mine.OwnerUserId,
            booking,
            Desk(mine.OwnerUserId),
            CancellationToken.None);

        // Assert: the same answer as a booking that is not there, so the desk
        // cannot be used to map what it cannot see.
        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            result.Failure.Should().Be(DeskFailure.BookingNotFound);
            stored.Status.Should().Be(BookingStatus.PendingVerification);
        }
    }

    [Fact]
    public async Task RejectAsync_ShouldPutTheHoursBackOnSaleAndTellTheCustomer()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Rejecting Desk Courts");
        var booking = await SubmittedAsync(context, venue, SevenAm);
        var letters = new RecordingNotifier();
        var sut = CreateService(context, letters);

        // Act
        var result = await sut.RejectAsync(
            venue.OwnerUserId,
            booking,
            new RejectBookingRequest(RejectReason.ReceiptUnclear, "  The receipt is for someone else's booking "),
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Assert
        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);
        var free = await CreateBookingServiceAsync(context, venue, SevenAm);

        using (new AssertionScope())
        {
            result.Succeeded.Should().BeTrue();
            stored.Status.Should().Be(BookingStatus.Rejected);
            // Picked from the list for the report, and one sentence for the
            // customer and the history, which already read this field.
            stored.RejectionReason.Should().Be(RejectReason.ReceiptUnclear);
            stored.RejectionNote.Should().Be("The receipt is for someone else's booking");
            stored.RejectedByUserId.Should().Be(venue.OwnerUserId);
            stored.CancellationReason.Should().Be("Receipt unclear — The receipt is for someone else's booking");
            // Its own state, not a cancellation: a customer changing their mind
            // and a receipt that did not add up read differently in a history.
            stored.Status.Should().NotBe(BookingStatus.Cancelled);
            // Rejected holds nothing, so the hour sells again.
            free.Succeeded.Should().BeTrue();
            // One letter, and it is the declined one, not a confirmation.
            letters.Declined.Should().Equal(booking);
            letters.Confirmed.Should().BeEmpty();
        }
    }

    /// <summary>
    /// The declined letter goes to the customer through the declined template,
    /// carrying the sentence they also see on their booking and how to reach
    /// the venue — they paid it, not IcyPlay.
    /// </summary>
    [Fact]
    public async Task RejectAsync_ShouldWriteToTheCustomerWithTheReasonAndTheVenuesContact()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Declined Letter Courts");
        var booking = await SubmittedAsync(context, venue, SevenAm);
        var mail = new CapturingEmailSender();
        var sut = CreateService(
            context,
            new BookingNotifier(
                context,
                mail,
                Options.Create(new BookingNotificationOptions
                {
                    BookingUrl = "https://icyplay.test/bookings",
                    SupportEmail = "help@icyplay.test"
                }),
                new FixedTimeProvider(Now),
                NullLogger<BookingNotifier>.Instance));
        var customerEmail = await context.Users
            .Where(user => user.Id == venue.CustomerUserId)
            .Select(user => user.Email)
            .SingleAsync();

        // Act
        await sut.RejectAsync(
            venue.OwnerUserId,
            booking,
            new RejectBookingRequest(RejectReason.WrongAmount, "Paid ₱300 only"),
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Assert
        var letter = mail.Sent.Should().ContainSingle().Subject;

        using (new AssertionScope())
        {
            letter.TemplateKey.Should().Be(EmailTemplateKey.BookingDeclined);
            letter.RecipientEmail.Should().Be(customerEmail);
            letter.Variables["decline_reason"].Should().Be("Wrong amount — Paid ₱300 only");
            letter.Variables["venue_contact"].Should().Be("+639171234567 · hello@example.com");
            letter.Variables["booking_url"].Should().Be($"https://icyplay.test/bookings/{booking}");
        }
    }

    /// <summary>
    /// A refusal is not made without a reason from the list, and Other is not
    /// a way of saying nothing — the report counts these.
    /// </summary>
    [Theory]
    [InlineData(null, 0, DeskFailure.RejectReasonRequired)]
    [InlineData("It was wrong", 0, DeskFailure.RejectReasonRequired)]
    [InlineData(RejectReason.Other, 0, DeskFailure.RejectNoteRequired)]
    [InlineData(RejectReason.WrongAmount, RejectReason.NoteLimit + 1, DeskFailure.RejectNoteTooLong)]
    public async Task RejectAsync_ShouldAskWhyBeforeTurningItDown(
        string? reason,
        int noteLength,
        DeskFailure expected)
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, $"Reject Reason Courts {(int)expected}");
        var booking = await SubmittedAsync(context, venue, SevenAm);
        var sut = CreateService(context);

        // Act
        var result = await sut.RejectAsync(
            venue.OwnerUserId,
            booking,
            new RejectBookingRequest(reason, noteLength == 0 ? "  " : new string('x', noteLength)),
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Assert: refused, and the payment is still waiting.
        context.ChangeTracker.Clear();
        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == booking);

        using (new AssertionScope())
        {
            result.Failure.Should().Be(expected);
            stored.Status.Should().Be(BookingStatus.PendingVerification);
        }
    }

    /// <summary>
    /// The declines report counts refusals on the venue's day, against every
    /// payment the desk answered, and shows an old typed refusal as not
    /// categorised with the words the desk wrote.
    /// </summary>
    [Fact]
    public async Task DeclinesAsync_ShouldCountRefusalsAgainstWhatTheDeskChecked()
    {
        // Arrange: one confirmed, one refused from the list, and one refused
        // before the list existed.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Declines Report Courts");
        var confirmed = await SubmittedAsync(context, venue, SevenAm);
        var refused = await SubmittedAsync(context, venue, EightAm);
        var typed = await SubmittedAsync(context, venue, new TimeOnly(9, 0));
        var sut = CreateService(context);

        await sut.ConfirmAsync(venue.OwnerUserId, confirmed, Desk(venue.OwnerUserId), CancellationToken.None);
        await sut.RejectAsync(
            venue.OwnerUserId,
            refused,
            new RejectBookingRequest(RejectReason.PaymentNotReceived),
            Desk(venue.OwnerUserId),
            CancellationToken.None);
        await context.Bookings
            .Where(row => row.Id == typed)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Status, BookingStatus.Rejected)
                .SetProperty(row => row.CancelledAt, Now)
                .SetProperty(row => row.CancellationReason, "No money came in"));

        // Somebody else's venue, refused the same day. Not this desk's count.
        var elsewhere = await VenueAsync(context, "Declines Report Elsewhere");
        var theirs = await SubmittedAsync(context, elsewhere, SevenAm);
        await sut.RejectAsync(
            elsewhere.OwnerUserId,
            theirs,
            new RejectBookingRequest(RejectReason.WrongAmount),
            Desk(elsewhere.OwnerUserId),
            CancellationToken.None);
        context.ChangeTracker.Clear();

        // Act
        var report = await sut.DeclinesAsync(
            venue.OwnerUserId,
            new HoursQuery(Today, Today, HoursGrain.Day),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            report.Succeeded.Should().BeTrue();
            var declines = report.Value!;

            declines.Total.Should().Be(2);
            declines.Checked.Should().Be(3);
            declines.Periods.Should().ContainSingle()
                .Which.Should().Match<DeclinesPeriod>(period => period.Declined == 2 && period.Checked == 3);
            declines.Periods.Single().Reasons.Select(count => count.Reason).Should().Equal(
                [.. RejectReason.All, null]);
            declines.Reasons.Should().BeEquivalentTo(
                [new ReasonCount(RejectReason.PaymentNotReceived, 1), new ReasonCount(null, 1)]);

            var picked = declines.Declines.Single(row => row.BookingId == refused);
            picked.Reason.Should().Be(RejectReason.PaymentNotReceived);
            picked.Note.Should().BeNull();
            picked.DeclinedOn.Should().Be(Today);
            picked.DeclinedByOwner.Should().BeTrue();
            picked.DeclinedByName.Should().NotBeNullOrEmpty();
            picked.Hours.Should().Be(1);
            picked.StartsAt.Should().Be(EightAm);
            picked.Amount.Should().BePositive();

            var old = declines.Declines.Single(row => row.BookingId == typed);
            old.Reason.Should().BeNull();
            old.Note.Should().Be("No money came in");
            old.DeclinedByName.Should().BeNull();
        }
    }

    /// <summary>
    /// An attendant reads the venue's money only once the owner says so, and
    /// only the owner can say so. Until then the money reports are refused and
    /// the rental is left out of the response rather than hidden by the page.
    /// </summary>
    [Fact]
    public async Task AttendantShouldSeeTheMoneyOnlyOnceTheOwnerSharesIt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Money Sharing Courts");
        var attendant = await AttendantAsync(context, venue.FacilityId);
        var attendantId = await context.FacilityAttendants
            .Where(row => row.UserId == attendant)
            .Select(row => row.Id)
            .SingleAsync();
        var sut = CreateService(context);
        var range = new HoursQuery(Tuesday, Tuesday, HoursGrain.Day);

        // Act: before, the attendant tries to share it with themselves, then the owner does.
        var takingsBefore = await sut.TakingsAsync(attendant, range, CancellationToken.None);
        var missedBefore = await sut.MissedAsync(attendant, range, CancellationToken.None);
        var rentalBefore = await sut.UtilizationAsync(attendant, new UtilizationQuery(Tuesday, Tuesday), CancellationToken.None);
        var venuesBefore = await sut.VenuesAsync(attendant, CancellationToken.None);
        var listByAttendant = await sut.AttendantsAsync(attendant, CancellationToken.None);
        var selfService = await sut.SetAttendantMoneyAsync(
            attendant, attendantId, true, Desk(attendant), CancellationToken.None);

        var roster = await sut.AttendantsAsync(venue.OwnerUserId, CancellationToken.None);
        var shared = await sut.SetAttendantMoneyAsync(
            venue.OwnerUserId, attendantId, true, Desk(venue.OwnerUserId), CancellationToken.None);
        context.ChangeTracker.Clear();

        var takingsAfter = await sut.TakingsAsync(attendant, range, CancellationToken.None);
        var rentalAfter = await sut.UtilizationAsync(attendant, new UtilizationQuery(Tuesday, Tuesday), CancellationToken.None);
        var venuesAfter = await sut.VenuesAsync(attendant, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            takingsBefore.Failure.Should().Be(DeskFailure.MoneyHidden);
            missedBefore.Failure.Should().Be(DeskFailure.MoneyHidden);
            rentalBefore.Value!.Rental.Should().BeNull();
            venuesBefore.Should().ContainSingle().Which.CanSeeMoney.Should().BeFalse();

            // Only the owner decides.
            listByAttendant.Failure.Should().Be(DeskFailure.NotOwner);
            selfService.Failure.Should().Be(DeskFailure.AttendantNotFound);

            roster.Value.Should().ContainSingle(row => row.Id == attendantId)
                .Which.CanSeeMoney.Should().BeFalse();
            shared.Value!.CanSeeMoney.Should().BeTrue();

            takingsAfter.Succeeded.Should().BeTrue();
            rentalAfter.Value!.Rental.Should().NotBeNull();
            venuesAfter.Should().ContainSingle().Which.CanSeeMoney.Should().BeTrue();
            (await context.AuditLogs.CountAsync(entry =>
                entry.Action == AuditAction.FacilityAttendantMoneyAccessChanged
                && entry.EntityId == venue.FacilityId)).Should().Be(1);
        }
    }

    [Fact]
    public async Task ListAsync_ShouldKeepWhatIsDoneApartFromWhatIsWaiting()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Two Tab Desk Courts");
        var confirmed = await SubmittedAsync(context, venue, SevenAm);
        await SubmittedAsync(context, venue, EightAm);
        var sut = CreateService(context);
        await sut.ConfirmAsync(venue.OwnerUserId, confirmed, Desk(venue.OwnerUserId), CancellationToken.None);

        // Act
        var waiting = await sut.ListAsync(venue.OwnerUserId, new DeskQuery(), CancellationToken.None);
        var done = await sut.ListAsync(
            venue.OwnerUserId,
            new DeskQuery(DeskTab.Confirmed),
            CancellationToken.None);

        // Assert: three that need doing must not be buried under fifty that are
        // done.
        using (new AssertionScope())
        {
            waiting.Value!.Items.Should().ContainSingle().Which.Id.Should().NotBe(confirmed);
            done.Value!.Items.Should().ContainSingle().Which.Id.Should().Be(confirmed);
        }
    }

    [Fact]
    public async Task ListAsync_ShouldPageAndPutTheLongestWaitFirst()
    {
        // Arrange: three, submitted in order.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Paged Desk Courts");
        var first = await SubmittedAsync(context, venue, SevenAm, Now);
        var second = await SubmittedAsync(context, venue, EightAm, Now.AddMinutes(5));
        await SubmittedAsync(context, venue, new TimeOnly(9, 0), Now.AddMinutes(10));
        var sut = CreateService(context);

        // Act
        var page = await sut.ListAsync(
            venue.OwnerUserId,
            new DeskQuery(PageSize: 2),
            CancellationToken.None);

        // Assert: somebody who paid an hour ago must not sit behind somebody who
        // paid a minute ago.
        using (new AssertionScope())
        {
            page.Value!.TotalItems.Should().Be(3);
            page.Value!.TotalPages.Should().Be(2);
            page.Value!.Items.Select(booking => booking.Id).Should().Equal(first, second);
        }
    }

    [Fact]
    public async Task VenuesAsync_ShouldListWhatSomebodyMayStandAt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "Alpha Desk Courts");
        await VenueAsync(context, "Beta Desk Courts");
        var sut = CreateService(context);

        // Act
        var venues = await sut.VenuesAsync(mine.OwnerUserId, CancellationToken.None);

        // Assert
        venues.Should().ContainSingle().Which.Name.Should().Be("Alpha Desk Courts");
    }

    [Fact]
    public async Task CourtsAsync_ShouldNameEveryPartTheFloorIsSoldIn()
    {
        // Arrange: one floor, basketball whole and pickleball three across.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Diary Courts");
        var sut = CreateService(context);

        // Act
        var courts = await sut.CourtsAsync(venue.OwnerUserId, CancellationToken.None);

        // Assert
        var court = courts.Single(candidate => candidate.Id == venue.CourtId);

        using (new AssertionScope())
        {
            court.Name.Should().Be("Desk court 1");
            // A whole floor is called by its sport; a divided one carries the
            // number, because "Pickleball" three times over names nothing.
            court.Units.Select(unit => unit.Label)
                .Should().BeEquivalentTo(
                    ["Basketball", "Pickleball 1", "Pickleball 2", "Pickleball 3"]);
        }
    }

    [Fact]
    public async Task ScheduleAsync_ShouldDrawEveryHourThatStillHoldsTheCourt()
    {
        // Arrange: one waiting on the desk, one confirmed.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Drawn Courts");
        await SubmittedAsync(context, venue, SevenAm);
        var confirmed = await SubmittedAsync(context, venue, EightAm);
        var sut = CreateService(context);
        await sut.ConfirmAsync(venue.OwnerUserId, confirmed, Desk(venue.OwnerUserId), CancellationToken.None);

        // Act
        var diary = await sut.ScheduleAsync(
            venue.OwnerUserId,
            venue.CourtId,
            Tuesday,
            Tuesday,
            CancellationToken.None);

        // Assert
        var hours = diary.Value!;

        using (new AssertionScope())
        {
            hours.Should().HaveCount(2);
            hours.Select(hour => hour.StartsAt).Should().Equal(SevenAm, EightAm);
            hours.Select(hour => hour.Status)
                .Should().BeEquivalentTo(
                    [nameof(BookingStatus.PendingVerification), nameof(BookingStatus.Confirmed)]);
            // Enough to draw a square and name it, and no more.
            hours.First().CustomerName.Should().Be("Booking Bianca");
            hours.First().UnitLabel.Should().Be("Basketball");
        }
    }

    [Fact]
    public async Task ScheduleAsync_ShouldLeaveOutAHoldWhoseClockRanOut()
    {
        // Arrange: booked and never paid for.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Lapsed Diary Courts");
        // Taken two hours ago, on a thirty-minute hold, and never paid for.
        var bookings = CreateBookingService(context, Now.AddHours(-2));
        var held = await bookings.CreateAsync(
            new CreateBookingRequest(
                venue.BookableCourtId,
                BookingKind.Hourly,
                [new BookingSlotInput(Tuesday, SevenAm)]),
            venue.CustomerUserId,
            CancellationToken.None);

        var sut = CreateService(context);

        // Act
        var diary = await sut.ScheduleAsync(
            venue.OwnerUserId,
            venue.CourtId,
            Tuesday,
            Tuesday,
            CancellationToken.None);

        // Assert: it holds nothing, and an hour drawn as taken that anybody can
        // book sends the desk away from an hour it could have sold.
        var stored = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == held.Value!.Id);

        using (new AssertionScope())
        {
            stored.HoldsUntil.Should().BeBefore(Now);
            stored.HasLapsedAt(Now).Should().BeTrue();
            diary.Value!.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ScheduleAsync_ShouldRefuseACourtAtSomebodyElsesVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "Mine Diary Courts");
        var theirs = await VenueAsync(context, "Theirs Diary Courts");
        var sut = CreateService(context);

        // Act
        var diary = await sut.ScheduleAsync(
            mine.OwnerUserId,
            theirs.CourtId,
            Tuesday,
            Tuesday,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            diary.Succeeded.Should().BeFalse();
            diary.Failure.Should().Be(DeskFailure.NotAttended);
        }
    }

    [Fact]
    public async Task ScheduleAsync_ShouldRefuseAStretchWiderThanADiaryDraws()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Wide Diary Courts");
        var sut = CreateService(context);

        // Act: a year, one hour per row.
        var diary = await sut.ScheduleAsync(
            venue.OwnerUserId,
            venue.CourtId,
            Tuesday,
            Tuesday.AddYears(1),
            CancellationToken.None);

        // Assert: that is a report, and a report should not arrive as a diary.
        diary.Failure.Should().Be(DeskFailure.WindowTooWide);
    }

    [Fact]
    public async Task CourtBookingsAsync_ShouldAnswerForWhatFellThroughAsWellAsWhatStands()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Fell Through Courts");
        var standing = await SubmittedAsync(context, venue, SevenAm);
        var turnedDown = await SubmittedAsync(context, venue, EightAm);
        var sut = CreateService(context);
        await sut.RejectAsync(
            venue.OwnerUserId,
            turnedDown,
            new RejectBookingRequest(RejectReason.WrongAmount),
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Act
        var everything = await sut.CourtBookingsAsync(
            venue.OwnerUserId,
            new CourtBookingQuery(venue.CourtId),
            CancellationToken.None);

        var rejected = await sut.CourtBookingsAsync(
            venue.OwnerUserId,
            new CourtBookingQuery(venue.CourtId, Status: nameof(BookingStatus.Rejected)),
            CancellationToken.None);

        // Assert: the diary draws what holds the court; the list is where a
        // venue goes looking for what did not.
        using (new AssertionScope())
        {
            everything.Value!.TotalItems.Should().Be(2);
            everything.Value!.Items.Select(booking => booking.Id)
                .Should().BeEquivalentTo([standing, turnedDown]);
            rejected.Value!.Items.Should().ContainSingle()
                .Which.DecisionReason.Should().Be("Wrong amount.");
            // Grouped by the part of the floor it was sold on, so the console
            // can file it under "Basketball" without asking again.
            everything.Value!.Items.Should().AllSatisfy(booking =>
                booking.BookableCourtId.Should().Be(venue.BookableCourtId));
        }
    }

    [Fact]
    public async Task CourtBookingsAsync_ShouldRefuseAStatusThatIsNotOne()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Bad Status Courts");
        var sut = CreateService(context);

        // Act
        var result = await sut.CourtBookingsAsync(
            venue.OwnerUserId,
            new CourtBookingQuery(venue.CourtId, Status: "Slightly Booked"),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(DeskFailure.UnknownStatus);
    }

    [Fact]
    public async Task BookingAsync_ShouldRefuseOneAtSomebodyElsesVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var mine = await VenueAsync(context, "My Click Courts");
        var theirs = await VenueAsync(context, "Their Click Courts");
        var booking = await SubmittedAsync(context, theirs, SevenAm);
        var sut = CreateService(context);

        // Act
        var mineResult = await sut.BookingAsync(mine.OwnerUserId, booking, CancellationToken.None);
        var theirsResult = await sut.BookingAsync(theirs.OwnerUserId, booking, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            mineResult.Failure.Should().Be(DeskFailure.BookingNotFound);
            theirsResult.Succeeded.Should().BeTrue();
            theirsResult.Value!.CourtId.Should().Be(theirs.CourtId);
        }
    }

    // ------------------------------------------------------------- the set-up

    /// <summary>A booking that has been paid for and handed to the venue.</summary>
    [Fact]
    public async Task SettingsAsync_ShouldStartAtThePlatformDefaults()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Settings Courts");
        var attendant = await AttendantAsync(context, venue.FacilityId);
        var sut = CreateService(context);

        // Act
        var settings = await sut.SettingsAsync(attendant, CancellationToken.None);

        // Assert: five minutes to pay and three moves, until the venue says
        // otherwise. The range travels with them, so the panel can say what is
        // possible rather than refusing after the fact.
        using (new AssertionScope())
        {
            settings.Succeeded.Should().BeTrue();
            settings.Value!.PartialBookingExpiryMinutes.Should().Be(PaymentHold.DefaultMinutes);
            settings.Value.MoveLimit.Should().Be(BookingMove.DefaultLimit);
            settings.Value.SmallestExpiry.Should().Be(PaymentHold.MinimumMinutes);
            settings.Value.LargestMoveLimit.Should().Be(BookingMove.LargestLimit);
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_ShouldLetAnAttendantSetBothDials()
    {
        // Arrange: an attendant rather than the owner. They are the one
        // standing there when a customer says the hold is too short.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Dialled Courts");
        var attendant = await AttendantAsync(context, venue.FacilityId);
        var sut = CreateService(context);

        // Act
        var saved = await sut.UpdateSettingsAsync(
            attendant,
            new UpdateDeskSettingsRequest(20, 5),
            Desk(attendant),
            CancellationToken.None);

        // Assert
        var stored = await context.FacilityOwners
            .AsNoTracking()
            .SingleAsync(owner => owner.UserId == venue.OwnerUserId);

        using (new AssertionScope())
        {
            saved.Succeeded.Should().BeTrue();
            stored.PartialBookingExpiryMinutes.Should().Be(20);
            stored.MoveLimit.Should().Be(5);
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_ShouldSetTheNoticeAndWindowOnlyWhenTheyAreSent()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Notice Dial Courts");
        var sut = CreateService(context);

        // Act: set it, then save again from a caller that does not know it.
        var set = await sut.UpdateSettingsAsync(
            venue.OwnerUserId,
            new UpdateDeskSettingsRequest(PaymentHold.DefaultMinutes, BookingMove.DefaultLimit, 5, 21),
            Desk(venue.OwnerUserId),
            CancellationToken.None);
        var kept = await sut.UpdateSettingsAsync(
            venue.OwnerUserId,
            new UpdateDeskSettingsRequest(20, BookingMove.DefaultLimit),
            Desk(venue.OwnerUserId),
            CancellationToken.None);
        var clamped = await sut.UpdateSettingsAsync(
            venue.OwnerUserId,
            new UpdateDeskSettingsRequest(20, BookingMove.DefaultLimit, 30, 90),
            Desk(venue.OwnerUserId),
            CancellationToken.None);

        // Assert: left out means left alone, and a month is a week.
        using (new AssertionScope())
        {
            set.Value!.MoveNoticeDays.Should().Be(5);
            set.Value.BookingWindowDays.Should().Be(21);
            kept.Value!.MoveNoticeDays.Should().Be(5);
            kept.Value.BookingWindowDays.Should().Be(21);
            clamped.Value!.MoveNoticeDays.Should().Be(BookingMove.LargestNoticeDays);
            clamped.Value.BookingWindowDays.Should().Be(BookingWindow.LargestDays);
            clamped.Value.SmallestMoveNoticeDays.Should().Be(BookingMove.SmallestNoticeDays);
        }
    }

    [Fact]
    public async Task SettingsHistoryAsync_ShouldSayWhoChangedWhichDialAndFromWhat()
    {
        // Arrange: an attendant shortens the hold, then the platform sets the
        // move rules on the venue's behalf.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Settings History Courts");
        var attendant = await AttendantAsync(context, venue.FacilityId);
        var sut = CreateService(context);

        await sut.UpdateSettingsAsync(
            attendant,
            new UpdateDeskSettingsRequest(20, BookingMove.DefaultLimit),
            Desk(attendant),
            CancellationToken.None);

        var owner = await context.FacilityOwners.SingleAsync(row => row.UserId == venue.OwnerUserId);
        var before = owner.MoveNoticeDays;
        owner.SetMoveNotice(4, Now.AddMinutes(1));
        new AuditLogger(context, new FixedTimeProvider(Now.AddMinutes(1))).RecordChange(
            new AuditActor(Guid.NewGuid(), UserRoleName.PlatformAdmin),
            AuditAction.FacilityOwnerMoveRulesUpdated,
            AuditEntityType.FacilityOwner,
            owner.Id,
            new Dictionary<string, string?> { ["moveNoticeDays"] = before.ToString() },
            new Dictionary<string, string?> { ["moveNoticeDays"] = "4" },
            "Owner rang");
        await context.SaveChangesAsync();

        // Act
        var history = await sut.SettingsHistoryAsync(venue.OwnerUserId, CancellationToken.None);

        // Assert: newest first, each dial from and to, and the platform's own
        // change marked as the platform's.
        using (new AssertionScope())
        {
            history.Succeeded.Should().BeTrue();
            var entries = history.Value!.ToArray();
            entries.Should().HaveCount(2);

            entries[0].ByPlatform.Should().BeTrue();
            entries[0].Reason.Should().Be("Owner rang");
            entries[0].Changes.Should().ContainSingle()
                .Which.Should().Be(new DeskSettingChange("moveNoticeDays", before.ToString(), "4"));

            entries[1].ByPlatform.Should().BeFalse();
            entries[1].ChangedBy.Should().NotBeNullOrWhiteSpace();
            entries[1].Changes.Should().ContainSingle()
                .Which.Setting.Should().Be("partialBookingExpiryMinutes");
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_ShouldClampRatherThanRefuse()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Clamped Courts");
        var attendant = await AttendantAsync(context, venue.FacilityId);
        var sut = CreateService(context);

        // Act: a day is not a hold, and neither is no moves at all.
        var saved = await sut.UpdateSettingsAsync(
            attendant,
            new UpdateDeskSettingsRequest(9999, 0),
            Desk(attendant),
            CancellationToken.None);

        // Assert: a dial is a dial. Somebody typing 9999 means "the longest you
        // allow", not "fail and lose what I typed".
        using (new AssertionScope())
        {
            saved.Value!.PartialBookingExpiryMinutes.Should().Be(PaymentHold.MaximumMinutes);
            saved.Value.MoveLimit.Should().Be(BookingMove.SmallestLimit);
        }
    }

    [Fact]
    public async Task SettingsAsync_ShouldRefuseSomebodyWhoDoesNotWorkTheVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        await VenueAsync(context, "Private Settings Courts");
        var sut = CreateService(context);

        // Act
        var settings = await sut.SettingsAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert: a desk somebody does not work answers the same as one that is
        // not there.
        settings.Failure.Should().Be(DeskFailure.NotAttended);
    }

    private static async Task<Guid> SubmittedAsync(
        AppDbContext context,
        Venue venue,
        TimeOnly hour,
        DateTimeOffset? submittedAt = null)
    {
        var bookings = CreateBookingService(context);

        var created = await bookings.CreateAsync(
            new CreateBookingRequest(
                venue.BookableCourtId,
                BookingKind.Hourly,
                [new BookingSlotInput(Tuesday, hour)]),
            venue.CustomerUserId,
            CancellationToken.None);

        var booking = await context.Bookings.SingleAsync(row => row.Id == created.Value!.Id);
        booking.AttachReceipt(
            "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg",
            submittedAt ?? Now);
        booking.SubmitForVerification(submittedAt ?? Now);
        await context.SaveChangesAsync();

        return booking.Id;
    }

    /// <summary>Whether the hour is on sale again, asked by trying to buy it.</summary>
    private static async Task<BookingResult<BookingDetail>> CreateBookingServiceAsync(
        AppDbContext context,
        Venue venue,
        TimeOnly hour) =>
        await CreateBookingService(context).CreateAsync(
            new CreateBookingRequest(
                venue.BookableCourtId,
                BookingKind.Hourly,
                [new BookingSlotInput(Tuesday, hour)]),
            venue.CustomerUserId,
            CancellationToken.None);

    /// <summary>Somebody put on this venue's desk, and their user id.</summary>
    private static async Task<Guid> AttendantAsync(AppDbContext context, Guid facilityId)
    {
        var user = new User($"attendant-{Guid.NewGuid():N}@example.com", "Desk Dahlia", null);
        user.SetPasswordHash("hash");
        context.Users.Add(user);
        context.UserRoles.Add(new UserRole(user.Id, UserRoleName.FacilityAttendant));
        context.FacilityAttendants.Add(new FacilityAttendant(facilityId, user.Id, Now));
        await context.SaveChangesAsync();

        return user.Id;
    }

    /// <summary>
    /// One venue with one bookable court, an owner who can be signed in as, and
    /// a customer with an actual account — the desk shows who booked it, so a
    /// made-up id would not do.
    /// </summary>
    private static async Task<Venue> VenueAsync(AppDbContext context, string facilityName)
    {
        var courts = CreateCourtService(context);
        var basketball = await context.Sports
            .Where(sport => sport.Key == "basketball")
            .Select(sport => sport.Id)
            .SingleAsync();

        var pickleball = await context.Sports
            .Where(sport => sport.Key == "pickleball")
            .Select(sport => sport.Id)
            .SingleAsync();

        var ownerUser = new User($"desk-owner-{Guid.NewGuid():N}@example.com", "Desk Owner", null);
        ownerUser.SetPasswordHash("hash");
        context.Users.Add(ownerUser);
        context.UserRoles.Add(new UserRole(ownerUser.Id, UserRoleName.FacilityOwner));

        var customer = new User($"desk-booker-{Guid.NewGuid():N}@example.com", "Booking Bianca", null);
        customer.SetPasswordHash("hash");
        context.Users.Add(customer);
        context.UserRoles.Add(new UserRole(customer.Id, UserRoleName.Customer));

        var owner = new FacilityOwner(ownerUser.Id, "Desk Ventures", "billing@example.com", null);
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
                NewFacility(facilityName),
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
                    new SportPricingInput(pickleball, 500m, 600m, 550m, 700m)
                ],
                new PeakWindowInput(new TimeOnly(17, 0), new TimeOnly(20, 0), true, true),
                "Opening rates"),
            Admin(),
            CancellationToken.None);

        var unit = await context.BookableCourts
            .AsNoTracking()
            .Where(candidate => candidate.CourtId == created.Value.CourtId
                && candidate.CourtSport.SportId == basketball)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        var facilityId = await context.Courts
            .AsNoTracking()
            .Where(court => court.Id == created.Value.CourtId)
            .Select(court => court.FacilityId)
            .SingleAsync();

        return new Venue(facilityId, created.Value.CourtId, unit, ownerUser.Id, customer.Id);
    }

    private sealed record Venue(
        Guid FacilityId,
        Guid CourtId,
        Guid BookableCourtId,
        Guid OwnerUserId,
        Guid CustomerUserId);

    private static DeskService CreateService(
        AppDbContext context,
        IBookingNotifier? notifier = null) => new(
        context,
        notifier ?? new RecordingNotifier(),
        new AuditLogger(context, new FixedTimeProvider(Now)),
        new FixedTimeProvider(Now),
        NullLogger<DeskService>.Instance);

    private static BookingService CreateBookingService(
        AppDbContext context,
        DateTimeOffset? now = null) => new(
        context,
        Assets(),
        new RecordingNotifier(),
        new AuditLogger(context, new FixedTimeProvider(now ?? Now)),
        new FixedTimeProvider(now ?? Now),
        NullLogger<BookingService>.Instance);

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

    private static AuditActor Desk(Guid userId) => new(userId, UserRoleName.FacilityOwner);

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

    /// <summary>
    /// Records what was written to the customer, so a test can ask whether the
    /// one letter went once rather than trusting that it did.
    /// </summary>
    private sealed class RecordingNotifier : IBookingNotifier
    {
        public List<Guid> Confirmed { get; } = [];

        /// <summary>Bookings the customer was told had been turned down.</summary>
        public List<Guid> Declined { get; } = [];

        /// <summary>Upgrades the customer was told had gone through.</summary>
        public List<Guid> UpgradesApproved { get; } = [];

        public Task PaymentSubmittedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;

        public Task BookingConfirmedAsync(Booking booking, CancellationToken ct)
        {
            Confirmed.Add(booking.Id);
            return Task.CompletedTask;
        }

        public Task BookingDeclinedAsync(Booking booking, CancellationToken ct)
        {
            Declined.Add(booking.Id);
            return Task.CompletedTask;
        }

        public Task UpgradeSubmittedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) =>
            Task.CompletedTask;

        public Task UpgradeApprovedAsync(BookingUpgradeRequest upgrade, CancellationToken ct)
        {
            UpgradesApproved.Add(upgrade.Id);
            return Task.CompletedTask;
        }

        /// <summary>Free moves the customer was told the venue agreed to.</summary>
        public List<Guid> MovesApproved { get; } = [];

        /// <summary>Moves, free or paid for, the customer was told the venue turned down.</summary>
        public List<Guid> MovesDeclined { get; } = [];

        public Task MoveRequestedAsync(BookingUpgradeRequest move, CancellationToken ct) => Task.CompletedTask;

        public Task MoveApprovedAsync(BookingUpgradeRequest move, string fromCourtName, CancellationToken ct)
        {
            MovesApproved.Add(move.Id);
            return Task.CompletedTask;
        }

        public Task MoveDeclinedAsync(BookingUpgradeRequest move, CancellationToken ct)
        {
            MovesDeclined.Add(move.Id);
            return Task.CompletedTask;
        }
    }

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
