using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Payments;
using IcyPlay.Infrastructure.Persistence;
using IcyPlay.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.IntegrationTests.Persistence;

/// <summary>
/// A booking paid through the gateway: the checkout it opens, and what the
/// gateway's word does to it when it comes back.
///
/// The gateway itself is faked — it is the boundary — but everything on this
/// side of it is real: the booking service, the database, the unique index
/// that keeps one delivery from confirming twice.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OnlinePaymentTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Tuesday = new(2026, 9, 15);
    private static readonly TimeOnly SevenAm = new(7, 0);
    private const string ValidSignature = "signed-by-gateway";

    [Fact]
    public async Task StartCheckoutAsync_ShouldOpenOneCheckoutAndHandItBackWhenAskedAgain()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Checkout Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var first = await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        var second = await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.SubjectId == bookingId);

        using (new AssertionScope())
        {
            first.Succeeded.Should().BeTrue();
            second.Value!.CheckoutUrl.Should().Be(first.Value!.CheckoutUrl);
            gateway.CheckoutsOpened.Should().Be(1);
            payment.Status.Should().Be(OnlinePaymentStatus.Pending);
            payment.FacilityOwnerId.Should().Be(venue.FacilityOwnerId);
            payment.VenueAmount.Should().Be(500m);
            payment.PlatformFee.Should().Be(15m);
            gateway.LastRequest!.LineItems.Sum(item => item.Amount).Should().Be(515m);
            gateway.LastRequest.SuccessUrl.Should().EndWith($"/{bookingId}?payment=success");
        }
    }

    [Fact]
    public async Task StartCheckoutAsync_ShouldRefuseABookingPaidByReceipt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Receipt Courts", PaymentMode.Manual);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var result = await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Failure.Should().Be(PaymentFailure.NotPaidOnline);
            gateway.CheckoutsOpened.Should().Be(0);
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldConfirmTheBookingAndTellBothSidesWhenPaidInTime()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Paid In Time Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var notifier = new RecordingNotifier();
        var sut = CreateService(context, gateway, notifier);
        var checkout = await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_in_time", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);

        // Act
        var outcome = await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.Id == checkout.Value!.PaymentId);

        using (new AssertionScope())
        {
            outcome.Should().Be(WebhookOutcome.Accepted);
            booking.Status.Should().Be(BookingStatus.Confirmed);
            booking.PaidTotal.Should().Be(515m);
            payment.Status.Should().Be(OnlinePaymentStatus.Paid);
            payment.PaymentMethod.Should().Be("qrph");
            payment.ProcessingFee.Should().Be(6.90m);
            notifier.Confirmed.Should().ContainSingle().Which.Should().Be(bookingId);
            notifier.PaidOnline.Should().ContainSingle().Which.Should().Be(bookingId);
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldActOnceWhenTheSameEventArrivesTwice()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Delivered Twice Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var notifier = new RecordingNotifier();
        var sut = CreateService(context, gateway, notifier);
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_twice", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);
        await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Act: a fresh context, the way a second request would arrive.
        await using var again = database.CreateContext();
        var secondOutcome = await CreateService(again, gateway, notifier)
            .HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        var events = await again.PaymentWebhookEvents.AsNoTracking().CountAsync(row => row.EventId == "evt_twice");

        using (new AssertionScope())
        {
            secondOutcome.Should().Be(WebhookOutcome.Accepted);
            events.Should().Be(1);
            notifier.Confirmed.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldHandThePaymentToAPersonWhenPaidAfterTheHoldRanOut()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Paid Late Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var notifier = new RecordingNotifier();
        var sut = CreateService(context, gateway, notifier, now: Now.AddMinutes(21));
        await CreateService(context, gateway, notifier)
            .StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_late", gateway.LastSessionId!, Now.AddMinutes(20), net: 515m);

        // Act
        var outcome = await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.SubjectId == bookingId);

        using (new AssertionScope())
        {
            outcome.Should().Be(WebhookOutcome.Accepted);
            // Not confirmed, and not refunded: the venue decides.
            booking.Status.Should().Be(BookingStatus.PendingPayment);
            payment.Status.Should().Be(OnlinePaymentStatus.NeedsAttention);
            payment.AttentionReason.Should().Be(AttentionReason.PaidAfterHoldLapsed);
            notifier.Confirmed.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldNotConfirmWhenLessArrivedThanWasOwed()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Short Paid Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_short", gateway.LastSessionId!, Now.AddMinutes(5), net: 514.99m);

        // Act
        await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.SubjectId == bookingId);

        using (new AssertionScope())
        {
            booking.Status.Should().Be(BookingStatus.PendingPayment);
            payment.AttentionReason.Should().Be(AttentionReason.AmountShort);
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldChangeNothingWhenTheEventIsNotSigned()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Unsigned Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_forged", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);

        // Act
        var outcome = await sut.HandleWebhookAsync("{}", "forged", CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);

        using (new AssertionScope())
        {
            outcome.Should().Be(WebhookOutcome.Rejected);
            booking.Status.Should().Be(BookingStatus.PendingPayment);
        }
    }

    [Fact]
    public async Task AttachReceiptAsync_ShouldRefuseAReceiptForABookingPaidOnline()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "No Receipt Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);

        // Act
        var result = await CreateBookingService(context).AttachReceiptAsync(
            bookingId,
            venue.CustomerUserId,
            new AttachReceiptRequest("https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg"),
            CancellationToken.None);

        // Assert
        result.Failure.Should().Be(BookingFailure.PaidOnline);
    }

    [Fact]
    public async Task CreateAsync_ShouldHoldADirectBookingForTheContractsOnlineHold()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Long Hold Courts", PaymentMode.Direct);

        // Act
        var bookingId = await BookAsync(context, venue);

        // Assert
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);

        using (new AssertionScope())
        {
            booking.PaymentChannel.Should().Be(PaymentMode.Direct);
            booking.HoldsUntil.Should().Be(Now.AddMinutes(OnlineHold.DefaultMinutes));
        }
    }

    [Fact]
    public async Task DeskTransactions_ShouldListThePaymentAndClearTheBadgeOnceSeen()
    {
        // Arrange: one booking paid online at the owner's venue.
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Transactions Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_transactions", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);
        await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);
        var transactions = new DeskTransactionService(context, new FixedTimeProvider(Now.AddMinutes(10)));

        // Act
        var listed = await transactions.ListAsync(venue.OwnerUserId, null, 1, 20, CancellationToken.None);
        var before = await transactions.SummaryAsync(venue.OwnerUserId, CancellationToken.None);
        await transactions.MarkSeenAsync(venue.OwnerUserId, CancellationToken.None);
        var after = await transactions.SummaryAsync(venue.OwnerUserId, CancellationToken.None);

        // Assert
        var row = listed.Items.Single();

        using (new AssertionScope())
        {
            listed.TotalItems.Should().Be(1);
            row.BookingId.Should().Be(bookingId);
            row.Status.Should().Be(OnlinePaymentStatus.Paid);
            row.NetAmount.Should().Be(515m);
            row.CustomerName.Should().Be("Paying Paolo");
            row.FacilityName.Should().Be("Transactions Courts");
            row.Description.Should().Contain("Transactions Courts");
            row.IsNew.Should().BeTrue();
            before.Unseen.Should().Be(1);
            after.Unseen.Should().Be(0);
        }
    }

    /// <summary>
    /// Paid, and the webhook never came — a tunnel gone, a network fault.
    /// Asking the gateway directly confirms it, and the webhook arriving late
    /// afterwards finds nothing left to do.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldConfirmByAskingTheGatewayWhenTheWebhookNeverCame()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "No Webhook Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var notifier = new RecordingNotifier();
        var sut = CreateService(context, gateway, notifier);
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_late_webhook", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);

        // Act
        var verified = await sut.VerifyAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        await using var later = database.CreateContext();
        var lateWebhook = await CreateService(later, gateway, notifier)
            .HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);

        using (new AssertionScope())
        {
            verified.Should().BeTrue();
            booking.Status.Should().Be(BookingStatus.Confirmed);
            lateWebhook.Should().Be(WebhookOutcome.Accepted);
            notifier.Confirmed.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task VerifyAsync_ShouldLeaveTheBookingWaitingWhenNothingHasBeenPaid()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Not Paid Yet Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Act
        var verified = await sut.VerifyAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();

        using (new AssertionScope())
        {
            verified.Should().BeFalse();
            (await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId))
                .Status.Should().Be(BookingStatus.PendingPayment);
        }
    }

    [Fact]
    public async Task Receipt_ShouldBreakOutTheGatewayFeeOnTopOfTheBookingTotal()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Receipt Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var receipts = new BookingReceiptService(context);
        var unpaid = await receipts.GetAsync(bookingId, venue.CustomerUserId, CancellationToken.None);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_receipt", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);
        await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);
        context.ChangeTracker.Clear();

        // Act
        var receipt = await receipts.GetAsync(bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            unpaid.Failure.Should().Be(BookingFailure.NotConfirmed);
            receipt.Succeeded.Should().BeTrue();
            receipt.Value!.ReceiptNumber.Should().StartWith("BK-");
            receipt.Value.BookingTotal.Should().Be(515m);
            receipt.Value.ProcessingFeeTotal.Should().Be(6.90m);
            receipt.Value.AmountPaid.Should().Be(521.90m);
            receipt.Value.Payments.Should().ContainSingle().Which.PaymentMethod.Should().Be("qrph");
        }
    }

    [Fact]
    public async Task DeskTransactions_ShouldShowNothingToSomebodyWhoDoesNotWorkTheVenue()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Other Desk Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = PaidEvent("evt_other_desk", gateway.LastSessionId!, Now.AddMinutes(5), net: 515m);
        await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);
        var transactions = new DeskTransactionService(context, new FixedTimeProvider(Now));

        // Act: the customer is no one's desk.
        var listed = await transactions.ListAsync(venue.CustomerUserId, null, 1, 20, CancellationToken.None);

        // Assert
        listed.TotalItems.Should().Be(0);
    }

    // ------------------------------------------------ paying on our own page

    [Fact]
    public async Task GetOptionsAsync_ShouldQuoteEachMethodAndShowNoAttemptYet()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Options Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var options = await sut.GetOptionsAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            options.Succeeded.Should().BeTrue();
            options.Value!.Experience.Should().Be(CheckoutExperience.Integrated);
            options.Value.AmountDue.Should().Be(515m);
            options.Value.Methods.Select(method => method.Total).Should().Equal(520m, 525m);
            options.Value.Current.Should().BeNull();
        }
    }

    [Fact]
    public async Task GetOptionsAsync_ShouldRefuseABookingPaidByReceipt()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Receipt Options Courts", PaymentMode.Manual);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var options = await sut.GetOptionsAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        var started = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            options.Failure.Should().Be(PaymentFailure.NotPaidOnline);
            started.Failure.Should().Be(PaymentFailure.NotPaidOnline);
            gateway.IntentsStarted.Should().Be(0);
        }
    }

    [Fact]
    public async Task StartMethodAsync_ShouldShowAQrCodeNoLongerThanTheHoldAndHandItBackWhenAskedAgain()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "QR Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var first = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);
        var second = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, " QRPH ", CancellationToken.None);
        var options = await sut.GetOptionsAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.SubjectId == bookingId);

        using (new AssertionScope())
        {
            first.Succeeded.Should().BeTrue();
            first.Value!.Method.Should().Be("qrph");
            first.Value.Total.Should().Be(520m);
            first.Value.Status.Should().Be(OnlinePaymentStatus.Pending);
            first.Value.QrCodeImage.Should().Be("data:image/png;base64,QR");
            first.Value.SecondsLeft.Should().Be(OnlineHold.DefaultMinutes * 60);
            second.Value!.PaymentId.Should().Be(first.Value.PaymentId);
            gateway.IntentsStarted.Should().Be(1);
            gateway.LastIntentRequest!.Amount.Should().Be(520m);
            gateway.LastIntentRequest.QrLifetime.Should().Be(TimeSpan.FromMinutes(OnlineHold.DefaultMinutes));
            payment.ChosenMethod.Should().Be("qrph");
            payment.QuotedTotal.Should().Be(520m);
            payment.CheckoutSessionId.Should().BeNull();
            options.Value!.Current!.PaymentId.Should().Be(first.Value.PaymentId);
        }
    }

    /// <summary>
    /// The code ran out and the gateway has not said so yet. The server's
    /// clock decides: the page is told it ended, and asking for QR Ph again
    /// makes a fresh code rather than handing back the dead one.
    /// </summary>
    [Fact]
    public async Task GetOptionsAsync_ShouldShowAQrCodeAsEndedOnceItsTimeIsUpByTheServersClock()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Ran Out Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated, QrLasts = TimeSpan.FromMinutes(5) };
        var first = await CreateService(context, gateway, new RecordingNotifier())
            .StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);
        context.ChangeTracker.Clear();
        var later = CreateService(context, gateway, new RecordingNotifier(), Now.AddMinutes(6));

        // Act
        var options = await later.GetOptionsAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        var again = await later.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            options.Value!.Current!.PaymentId.Should().Be(first.Value!.PaymentId);
            options.Value.Current.Status.Should().Be(OnlinePaymentStatus.Failed);
            options.Value.Current.FailureReason.Should().Be(PayMongoGateway.QrPhExpiredEvent);
            options.Value.Current.QrCodeImage.Should().BeNull();
            options.Value.Current.SecondsLeft.Should().BeNull();
            again.Value!.PaymentId.Should().NotBe(first.Value.PaymentId);
            gateway.IntentsStarted.Should().Be(2);
        }
    }

    [Fact]
    public async Task StartMethodAsync_ShouldSendTheCustomerToTheirEWalletAndBringThemBackHere()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "GCash Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var started = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "gcash", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            started.Value!.RedirectUrl.Should().StartWith("https://gcash.test/authorize/");
            started.Value.QrCodeImage.Should().BeNull();
            started.Value.SecondsLeft.Should().BeNull();
            started.Value.Total.Should().Be(525m);
            gateway.LastIntentRequest!.ReturnUrl.Should().EndWith($"/{bookingId}?payment=returned");
        }
    }

    [Fact]
    public async Task StartMethodAsync_ShouldRefuseAMethodTheVenuesPageDoesNotOffer()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Unknown Method Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var started = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "bitcoin", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            started.Failure.Should().Be(PaymentFailure.UnknownMethod);
            gateway.IntentsStarted.Should().Be(0);
            (await context.OnlinePayments.AsNoTracking().AnyAsync(row => row.SubjectId == bookingId)).Should().BeFalse();
        }
    }

    /// <summary>
    /// A card is paid on the gateway's own page, offering the card alone —
    /// and that page is never handed out as the one with every method.
    /// </summary>
    [Fact]
    public async Task StartMethodAsync_ShouldOpenACardOnlyCheckoutThatIsNeverHandedBackAsTheFullPage()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Card Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());

        // Act
        var card = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "card", CancellationToken.None);
        var cardRequest = gateway.LastRequest;
        var hosted = await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            card.Value!.Method.Should().Be("card");
            card.Value.RedirectUrl.Should().StartWith("https://checkout.test/");
            card.Value.Total.Should().Be(515m);
            cardRequest!.PaymentMethodTypes.Should().Equal("card");
            cardRequest.SuccessUrl.Should().EndWith("?payment=success");
            hosted.Value!.CheckoutUrl.Should().NotBe(card.Value.RedirectUrl);
            gateway.LastRequest!.PaymentMethodTypes.Should().BeNull();
            gateway.CheckoutsOpened.Should().Be(2);
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldConfirmABookingPaidOnOurOwnPage()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Own Page Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var notifier = new RecordingNotifier();
        var sut = CreateService(context, gateway, notifier);
        var started = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);
        gateway.Next = IntentPaidEvent("evt_own_page", gateway.LastIntentId!, Now.AddMinutes(3), net: 515m);

        // Act
        var outcome = await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var booking = await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId);
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.Id == started.Value!.PaymentId);

        using (new AssertionScope())
        {
            outcome.Should().Be(WebhookOutcome.Accepted);
            booking.Status.Should().Be(BookingStatus.Confirmed);
            payment.Status.Should().Be(OnlinePaymentStatus.Paid);
            payment.AmountCharged.Should().Be(520m);
            notifier.Confirmed.Should().ContainSingle().Which.Should().Be(bookingId);
        }
    }

    /// <summary>
    /// A hosted checkout's payment raises payment.paid as well as the
    /// checkout's own event. Only the checkout's is acted on.
    /// </summary>
    [Fact]
    public async Task HandleWebhookAsync_ShouldIgnoreAPaymentEventThatIsNotOneOfOurPagesPayments()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Hosted Echo Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway();
        var sut = CreateService(context, gateway, new RecordingNotifier());
        await sut.StartCheckoutAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        gateway.Next = IntentPaidEvent("evt_hosted_echo", "pi_belongs_to_a_checkout", Now.AddMinutes(3), net: 515m);

        // Act
        var outcome = await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            outcome.Should().Be(WebhookOutcome.Ignored);
            (await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId))
                .Status.Should().Be(BookingStatus.PendingPayment);
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldEndAnExpiredQrCodeAndLetTheCustomerStartAnother()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Expired QR Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());
        var first = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);
        gateway.Next = IntentEndedEvent("evt_qr_expired", PayMongoGateway.QrPhExpiredEvent, gateway.LastIntentId!, failureCode: null);

        // Act
        var outcome = await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);
        var options = await sut.GetOptionsAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);
        var again = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            outcome.Should().Be(WebhookOutcome.Accepted);
            options.Value!.Current!.Status.Should().Be(OnlinePaymentStatus.Failed);
            options.Value.Current.FailureReason.Should().Be(PayMongoGateway.QrPhExpiredEvent);
            options.Value.Current.QrCodeImage.Should().BeNull();
            again.Value!.PaymentId.Should().NotBe(first.Value!.PaymentId);
            again.Value.Status.Should().Be(OnlinePaymentStatus.Pending);
            gateway.IntentsStarted.Should().Be(2);
            (await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId))
                .Status.Should().Be(BookingStatus.PendingPayment);
        }
    }

    [Fact]
    public async Task HandleWebhookAsync_ShouldKeepTheGatewaysReasonWhenAnEWalletDeclines()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Declined Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());
        var started = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "gcash", CancellationToken.None);
        gateway.Next = IntentEndedEvent("evt_declined", PayMongoGateway.PaymentFailedEvent, gateway.LastIntentId!, "insufficient_funds");

        // Act
        await sut.HandleWebhookAsync("{}", ValidSignature, CancellationToken.None);

        // Assert
        var payment = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.Id == started.Value!.PaymentId);

        using (new AssertionScope())
        {
            payment.Status.Should().Be(OnlinePaymentStatus.Failed);
            payment.FailureReason.Should().Be("insufficient_funds");
        }
    }

    /// <summary>
    /// The QR code was shown, then the customer went to GCash instead — and
    /// paid the QR code after all. Asking the gateway finds it, whichever
    /// attempt it was.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFindAPaymentOnOurOwnPageWhicheverAttemptWasPaid()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Either Attempt Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var gateway = new FakeGateway { CheckoutExperience = CheckoutExperience.Integrated };
        var sut = CreateService(context, gateway, new RecordingNotifier());
        var qr = await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "qrph", CancellationToken.None);
        await sut.StartMethodAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, "gcash", CancellationToken.None);
        gateway.PaidIntentId = await context.OnlinePayments
            .Where(row => row.Id == qr.Value!.PaymentId)
            .Select(row => row.PaymentIntentId)
            .SingleAsync();
        gateway.Next = IntentPaidEvent("evt_unused", gateway.PaidIntentId!, Now.AddMinutes(4), net: 515m);

        // Act
        var verified = await sut.VerifyAsync(PaymentPurpose.Booking, bookingId, venue.CustomerUserId, CancellationToken.None);

        // Assert
        context.ChangeTracker.Clear();
        var paid = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.Status == OnlinePaymentStatus.Paid && row.SubjectId == bookingId);

        using (new AssertionScope())
        {
            verified.Should().BeTrue();
            paid.Id.Should().Be(qr.Value!.PaymentId);
            (await context.Bookings.AsNoTracking().SingleAsync(row => row.Id == bookingId))
                .Status.Should().Be(BookingStatus.Confirmed);
        }
    }

    /// <summary>
    /// A QR code that ran out, or an e-wallet that declined: nothing was paid,
    /// so it is no transaction of the venue's and no reason for a badge.
    /// </summary>
    [Fact]
    public async Task DeskTransactions_ShouldLeaveOutAnAttemptThatEndedUnpaid()
    {
        // Arrange
        await using var context = database.CreateContext();
        var venue = await VenueAsync(context, "Unpaid Attempt Courts", PaymentMode.Direct);
        var bookingId = await BookAsync(context, venue);
        var facilityId = await context.Facilities
            .Where(facility => facility.FacilityOwnerId == venue.FacilityOwnerId)
            .Select(facility => facility.Id)
            .SingleAsync();
        var attempt = new OnlinePayment(
            PaymentPurpose.Booking,
            bookingId,
            venue.FacilityOwnerId,
            venue.CustomerUserId,
            500m,
            15m,
            PayMongoGateway.ProviderName,
            Now,
            facilityId);
        attempt.OpenIntent($"pi_{Guid.NewGuid():N}", "qrph", 523.04m, "data:image/png;base64,QR", null, Now.AddMinutes(10), Now);
        attempt.EndUnpaid(PayMongoGateway.QrPhExpiredEvent, Now.AddMinutes(10));
        context.OnlinePayments.Add(attempt);
        await context.SaveChangesAsync();
        var transactions = new DeskTransactionService(context, new FixedTimeProvider(Now.AddMinutes(11)));

        // Act
        var listed = await transactions.ListAsync(venue.OwnerUserId, null, 1, 20, CancellationToken.None);
        var summary = await transactions.SummaryAsync(venue.OwnerUserId, CancellationToken.None);

        // Assert
        var saved = await context.OnlinePayments.AsNoTracking().SingleAsync(row => row.Id == attempt.Id);

        using (new AssertionScope())
        {
            listed.TotalItems.Should().Be(0);
            summary.Unseen.Should().Be(0);
            saved.Status.Should().Be(OnlinePaymentStatus.Failed);
            saved.ChosenMethod.Should().Be("qrph");
            saved.QuotedTotal.Should().Be(523.04m);
            saved.QrCodeImage.Should().BeNull();
        }
    }

    private static GatewayEvent IntentPaidEvent(string eventId, string intentId, DateTimeOffset paidAt, decimal net) =>
        new(
            eventId,
            PayMongoGateway.PaymentPaidEvent,
            LiveMode: false,
            CheckoutSessionId: null,
            new GatewayPaidPayment($"pay_{eventId}", "qrph", net + 5m, 5m, net, paidAt),
            intentId);

    private static GatewayEvent IntentEndedEvent(string eventId, string eventType, string intentId, string? failureCode) =>
        new(eventId, eventType, LiveMode: false, CheckoutSessionId: null, Payment: null, intentId, failureCode);

    private static GatewayEvent PaidEvent(string eventId, string sessionId, DateTimeOffset paidAt, decimal net) =>
        new(
            eventId,
            PayMongoGateway.CheckoutPaidEvent,
            LiveMode: false,
            sessionId,
            new GatewayPaidPayment($"pay_{eventId}", "qrph", net + 6.90m, 6.90m, net, paidAt));

    private static async Task<Guid> BookAsync(AppDbContext context, Venue venue)
    {
        var created = await CreateBookingService(context).CreateAsync(
            new CreateBookingRequest(
                venue.BookableCourtId,
                BookingKind.Hourly,
                [new BookingSlotInput(Tuesday, SevenAm)]),
            venue.CustomerUserId,
            CancellationToken.None);

        created.Succeeded.Should().BeTrue(created.Failure.ToString());

        return created.Value!.Id;
    }

    private static async Task<Venue> VenueAsync(AppDbContext context, string facilityName, string paymentMode)
    {
        var courts = CreateCourtService(context);
        var basketball = await context.Sports
            .Where(sport => sport.Key == "basketball")
            .Select(sport => sport.Id)
            .SingleAsync();

        var ownerUser = new User($"pay-owner-{Guid.NewGuid():N}@example.com", "Pay Owner", null);
        ownerUser.SetPasswordHash("hash");
        context.Users.Add(ownerUser);
        context.UserRoles.Add(new UserRole(ownerUser.Id, UserRoleName.FacilityOwner));

        var customer = new User($"pay-booker-{Guid.NewGuid():N}@example.com", "Paying Paolo", null);
        customer.SetPasswordHash("hash");
        context.Users.Add(customer);
        context.UserRoles.Add(new UserRole(customer.Id, UserRoleName.Customer));

        var owner = new FacilityOwner(ownerUser.Id, "Pay Ventures", "billing@example.com", null);
        context.FacilityOwners.Add(owner);

        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var contract = new FacilityOwnerContract(
            owner.Id,
            today.AddMonths(-1),
            today.AddMonths(11),
            Guid.NewGuid(),
            null,
            Now);
        contract.SetPaymentTerms(paymentMode, OnlineHold.DefaultMinutes, Now);
        context.FacilityOwnerContracts.Add(contract);
        await context.SaveChangesAsync();

        var created = await courts.CreateAsync(
            new CreateCourtRequest(
                owner.Id,
                null,
                NewFacility(facilityName),
                new CourtInput(
                    "Pay court 1",
                    10,
                    "The near court.",
                    [new CourtSportInput(basketball, 1)],
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
                [new SportPricingInput(basketball, 500m, 600m, 550m, 700m)],
                new PeakWindowInput(new TimeOnly(17, 0), new TimeOnly(20, 0), true, true),
                "Opening rates"),
            Admin(),
            CancellationToken.None);

        var unit = await context.BookableCourts
            .AsNoTracking()
            .Where(candidate => candidate.CourtId == created.Value.CourtId)
            .Select(candidate => candidate.Id)
            .SingleAsync();

        return new Venue(unit, owner.Id, customer.Id, ownerUser.Id);
    }

    private sealed record Venue(Guid BookableCourtId, Guid FacilityOwnerId, Guid CustomerUserId, Guid OwnerUserId);

    private static OnlinePaymentService CreateService(
        AppDbContext context,
        IPaymentGateway gateway,
        IBookingNotifier notifier,
        DateTimeOffset? now = null)
    {
        var clock = new FixedTimeProvider(now ?? Now);

        return new OnlinePaymentService(
            context,
            gateway,
            [
                new BookingPaymentHandler(
                    context,
                    notifier,
                    new AuditLogger(context, clock),
                    Options.Create(new BookingNotificationOptions { BookingUrl = "https://icyplay.test/bookings" }),
                    clock)
            ],
            clock,
            NullLogger<OnlinePaymentService>.Instance);
    }

    private static BookingService CreateBookingService(AppDbContext context) => new(
        context,
        Assets(),
        new RecordingNotifier(),
        new AuditLogger(context, new FixedTimeProvider(Now)),
        new FixedTimeProvider(Now),
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
    /// The gateway, as far as these tests need it: checkouts it opened, and the
    /// next event it will vouch for when the signature is the right one.
    /// </summary>
    private sealed class FakeGateway : IPaymentGateway
    {
        public int CheckoutsOpened
        {
            get; private set;
        }
        public string? LastSessionId
        {
            get; private set;
        }
        public CheckoutRequest? LastRequest
        {
            get; private set;
        }
        public GatewayEvent? Next
        {
            get; set;
        }

        public string Provider => PayMongoGateway.ProviderName;

        public bool IsConfigured => true;

        public int IntentsStarted
        {
            get; private set;
        }
        public PaymentIntentRequest? LastIntentRequest
        {
            get; private set;
        }
        public string? LastIntentId
        {
            get; private set;
        }

        /// <summary>How long a QR code lasts, when not as long as it was asked for.</summary>
        public TimeSpan? QrLasts
        {
            get; set;
        }

        /// <summary>The one intent the gateway says was paid, when asked about intents.</summary>
        public string? PaidIntentId
        {
            get; set;
        }

        public string CheckoutExperience { get; set; } = Application.Payments.CheckoutExperience.Hosted;

        /// <summary>Round fees, so a test reads its totals at a glance.</summary>
        public IReadOnlyList<PaymentMethodQuote> QuoteMethods(decimal amountDue) =>
        [
            new("qrph", amountDue, 5m, amountDue + 5m),
            new("gcash", amountDue, 10m, amountDue + 10m)
        ];

        public Task<CheckoutSessionCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
        {
            CheckoutsOpened++;
            LastRequest = request;
            LastSessionId = $"cs_{Guid.NewGuid():N}";

            return Task.FromResult(new CheckoutSessionCreated(LastSessionId, $"https://checkout.test/{LastSessionId}"));
        }

        public Task<GatewayPaidPayment?> GetPaidPaymentAsync(string checkoutSessionId, CancellationToken ct) =>
            Task.FromResult(Next?.Payment);

        public Task<PaymentIntentStarted> StartPaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct)
        {
            IntentsStarted++;
            LastIntentRequest = request;
            LastIntentId = $"pi_{Guid.NewGuid():N}";
            var isQr = request.Method == "qrph";

            return Task.FromResult(new PaymentIntentStarted(
                LastIntentId,
                isQr ? null : $"https://{request.Method}.test/authorize/{LastIntentId}",
                isQr ? "data:image/png;base64,QR" : null,
                isQr ? Now.Add(QrLasts ?? request.QrLifetime) : null));
        }

        public Task<GatewayPaidPayment?> GetPaidIntentPaymentAsync(string paymentIntentId, CancellationToken ct) =>
            Task.FromResult(PaidIntentId is null || PaidIntentId == paymentIntentId ? Next?.Payment : null);

        public GatewayEvent? ReadEvent(string rawBody, string? signatureHeader) =>
            signatureHeader == ValidSignature ? Next : null;
    }

    private sealed class RecordingNotifier : IBookingNotifier
    {
        public List<Guid> Confirmed { get; } = [];
        public List<Guid> PaidOnline { get; } = [];

        public Task PaymentSubmittedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;

        public Task UpgradeSubmittedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) => Task.CompletedTask;

        public Task UpgradeApprovedAsync(BookingUpgradeRequest upgrade, CancellationToken ct) => Task.CompletedTask;

        public Task MoveRequestedAsync(BookingUpgradeRequest move, CancellationToken ct) => Task.CompletedTask;

        public Task MoveApprovedAsync(BookingUpgradeRequest move, string fromCourtName, CancellationToken ct) =>
            Task.CompletedTask;

        public Task MoveDeclinedAsync(BookingUpgradeRequest move, CancellationToken ct) => Task.CompletedTask;

        public Task BookingConfirmedAsync(Booking booking, CancellationToken ct)
        {
            Confirmed.Add(booking.Id);
            return Task.CompletedTask;
        }

        public Task BookingPaidOnlineAsync(Booking booking, OnlinePayment payment, CancellationToken ct)
        {
            PaidOnline.Add(booking.Id);
            return Task.CompletedTask;
        }

        public Task BookingDeclinedAsync(Booking booking, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
