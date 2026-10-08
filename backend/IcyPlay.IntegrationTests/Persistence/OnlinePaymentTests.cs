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

        public Task<CheckoutSessionCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
        {
            CheckoutsOpened++;
            LastRequest = request;
            LastSessionId = $"cs_{Guid.NewGuid():N}";

            return Task.FromResult(new CheckoutSessionCreated(LastSessionId, $"https://checkout.test/{LastSessionId}"));
        }

        public Task<GatewayPaidPayment?> GetPaidPaymentAsync(string checkoutSessionId, CancellationToken ct) =>
            Task.FromResult(Next?.Payment);

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
