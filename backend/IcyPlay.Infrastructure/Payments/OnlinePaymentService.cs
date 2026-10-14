using IcyPlay.Application.Payments;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Payments;

/// <summary>
/// Opens checkouts and acts on what the gateway says about them, for every
/// kind of thing that is paid online. What each thing owes and what being paid
/// does to it belong to its <see cref="IPaymentPurposeHandler"/>; this is the
/// part that is the same for all of them.
/// </summary>
public sealed class OnlinePaymentService(
    AppDbContext db,
    IPaymentGateway gateway,
    IEnumerable<IPaymentPurposeHandler> handlers,
    TimeProvider timeProvider,
    ILogger<OnlinePaymentService> logger) : IOnlinePaymentService
{
    /// <summary>The card, paid on the gateway's own page even when the rest are paid on ours.</summary>
    public const string CardMethod = "card";

    /// <summary>
    /// The longest a QR code is offered for, as PayMongo has it by default.
    /// Shorter when the hold has less left: a code that outlives the court
    /// is a payment that arrives for hours already sold.
    /// </summary>
    private static readonly TimeSpan LongestQrLifetime = TimeSpan.FromMinutes(30);

    /// <summary>A code with less than this left is not handed back: it would run out mid-scan.</summary>
    private static readonly TimeSpan QrWorthReusing = TimeSpan.FromMinutes(1);

    public async Task<PaymentResult<CheckoutStarted>> StartCheckoutAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return PaymentResult<CheckoutStarted>.Fail(PaymentFailure.GatewayNotConfigured);
        }

        var handler = Handler(purpose);

        if (handler is null)
        {
            return PaymentResult<CheckoutStarted>.Fail(PaymentFailure.UnknownPurpose);
        }

        var described = await handler.DescribeAsync(subjectId, customerUserId, ct);

        if (!described.Succeeded)
        {
            return PaymentResult<CheckoutStarted>.Fail(described.Failure);
        }

        var payable = described.Value!;

        // The checkout already open for this, if it still asks for the same
        // money. A second one would be a second page the customer could pay
        // on, and a customer who pays both has paid twice. One opened for a
        // card alone, from our own page, is not this page.
        var open = await db.OnlinePayments
            .Where(payment => payment.Purpose == purpose
                && payment.SubjectId == subjectId
                && payment.Status == OnlinePaymentStatus.Pending
                && payment.CheckoutUrl != null
                && payment.ChosenMethod == null)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (open is not null && open.VenueAmount == payable.VenueAmount && open.PlatformFee == payable.PlatformFee)
        {
            return PaymentResult<CheckoutStarted>.Success(new CheckoutStarted(open.Id, open.CheckoutUrl!));
        }

        var now = timeProvider.GetUtcNow();
        var payment = NewPayment(purpose, subjectId, customerUserId, payable, now);
        var session = await OpenHostedCheckoutAsync(payment, payable, methods: null, ct);

        if (session is null)
        {
            return PaymentResult<CheckoutStarted>.Fail(PaymentFailure.GatewayUnavailable);
        }

        payment.OpenCheckout(session.SessionId, session.CheckoutUrl, now);
        db.OnlinePayments.Add(payment);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Checkout {SessionId} opened for {Purpose} {SubjectId}: {Amount} due.",
            session.SessionId,
            purpose,
            subjectId,
            payment.AmountDue);

        return PaymentResult<CheckoutStarted>.Success(new CheckoutStarted(payment.Id, session.CheckoutUrl));
    }

    public async Task<PaymentResult<PaymentOptions>> GetOptionsAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var handler = Handler(purpose);

        if (handler is null)
        {
            return PaymentResult<PaymentOptions>.Fail(PaymentFailure.UnknownPurpose);
        }

        var described = await handler.DescribeAsync(subjectId, customerUserId, ct);

        if (!described.Succeeded)
        {
            return PaymentResult<PaymentOptions>.Fail(described.Failure);
        }

        var payable = described.Value!;
        var amountDue = payable.VenueAmount + payable.PlatformFee;
        var now = timeProvider.GetUtcNow();

        var current = await db.OnlinePayments
            .AsNoTracking()
            .Where(payment => payment.Purpose == purpose
                && payment.SubjectId == subjectId
                && payment.CustomerUserId == customerUserId
                && payment.ChosenMethod != null
                && (payment.Status == OnlinePaymentStatus.Pending || payment.Status == OnlinePaymentStatus.Failed))
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return PaymentResult<PaymentOptions>.Success(new PaymentOptions(
            gateway.CheckoutExperience,
            amountDue,
            amountDue > 0m ? gateway.QuoteMethods(amountDue) : [],
            current is null ? null : Attempt(current, now)));
    }

    public async Task<PaymentResult<PaymentAttempt>> StartMethodAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        string method,
        CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return PaymentResult<PaymentAttempt>.Fail(PaymentFailure.GatewayNotConfigured);
        }

        var handler = Handler(purpose);

        if (handler is null)
        {
            return PaymentResult<PaymentAttempt>.Fail(PaymentFailure.UnknownPurpose);
        }

        var described = await handler.DescribeAsync(subjectId, customerUserId, ct);

        if (!described.Succeeded)
        {
            return PaymentResult<PaymentAttempt>.Fail(described.Failure);
        }

        var payable = described.Value!;
        var chosen = method.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();

        return chosen == CardMethod
            ? await StartCardAsync(purpose, subjectId, customerUserId, payable, now, ct)
            : await StartIntentAsync(purpose, subjectId, customerUserId, chosen, payable, now, ct);
    }

    public async Task<WebhookOutcome> HandleWebhookAsync(string rawBody, string? signatureHeader, CancellationToken ct)
    {
        var received = gateway.ReadEvent(rawBody, signatureHeader);

        if (received is null)
        {
            logger.LogWarning("A payment webhook arrived that was not signed by {Provider}. Ignored.", gateway.Provider);

            return WebhookOutcome.Rejected;
        }

        return received.EventType switch
        {
            PayMongoGateway.CheckoutPaidEvent when received.Payment is not null
                && !string.IsNullOrWhiteSpace(received.CheckoutSessionId) =>
                await CheckoutPaidAsync(received, ct),
            PayMongoGateway.PaymentPaidEvent when received.Payment is not null
                && !string.IsNullOrWhiteSpace(received.PaymentIntentId) =>
                await IntentPaidAsync(received, ct),
            PayMongoGateway.PaymentFailedEvent or PayMongoGateway.QrPhExpiredEvent
                when !string.IsNullOrWhiteSpace(received.PaymentIntentId) =>
                await IntentEndedUnpaidAsync(received, ct),
            _ => WebhookOutcome.Ignored
        };
    }

    /// <summary>
    /// Asks the gateway directly whether an attempt this customer opened has
    /// been paid, and settles it if so. The webhook is the usual way to hear;
    /// this is for when it has not arrived — a network fault, a restart, a
    /// tunnel gone — and the customer is sitting on "Confirming your payment".
    /// The same settlement as the webhook's, so whichever lands first wins and
    /// the other finds nothing left to do.
    ///
    /// Every open attempt is asked about, newest first: a customer who showed
    /// a QR code and then went to GCash may have paid either.
    /// </summary>
    public async Task<bool> VerifyAsync(string purpose, Guid subjectId, Guid customerUserId, CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return false;
        }

        var attempts = await db.OnlinePayments
            .Where(candidate => candidate.Purpose == purpose
                && candidate.SubjectId == subjectId
                && candidate.CustomerUserId == customerUserId
                && candidate.Status == OnlinePaymentStatus.Pending
                && (candidate.CheckoutSessionId != null || candidate.PaymentIntentId != null))
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Take(5)
            .ToListAsync(ct);

        foreach (var payment in attempts)
        {
            GatewayPaidPayment? paid;

            try
            {
                paid = payment.PaymentIntentId is not null
                    ? await gateway.GetPaidIntentPaymentAsync(payment.PaymentIntentId, ct)
                    : await gateway.GetPaidPaymentAsync(payment.CheckoutSessionId!, ct);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "Could not ask {Provider} about payment {PaymentId}.",
                    gateway.Provider,
                    payment.Id);

                continue;
            }

            if (paid is null)
            {
                continue;
            }

            var now = timeProvider.GetUtcNow();
            var (settlement, handler) = await ApplyPaidAsync(payment, paid, now, ct);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The webhook settled it in the same moment. Nothing left to do.
                db.ChangeTracker.Clear();

                return true;
            }

            logger.LogInformation(
                "Payment {PaymentId} found paid by asking {Provider}, not by webhook.",
                payment.Id,
                gateway.Provider);

            if (settlement.Settled && handler is not null)
            {
                await handler.AfterSettledAsync(payment, ct);
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// A card from our own page: the gateway's checkout, offering the card
    /// alone, with its fee passed on there as it always has been — the card's
    /// fee has a fixed part and a foreign-card rate we do not work out.
    /// </summary>
    private async Task<PaymentResult<PaymentAttempt>> StartCardAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        Payable payable,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var open = await db.OnlinePayments
            .Where(payment => payment.Purpose == purpose
                && payment.SubjectId == subjectId
                && payment.CustomerUserId == customerUserId
                && payment.Status == OnlinePaymentStatus.Pending
                && payment.ChosenMethod == CardMethod
                && payment.CheckoutUrl != null)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (open is not null && open.VenueAmount == payable.VenueAmount && open.PlatformFee == payable.PlatformFee)
        {
            return PaymentResult<PaymentAttempt>.Success(Attempt(open, now));
        }

        var payment = NewPayment(purpose, subjectId, customerUserId, payable, now);
        var session = await OpenHostedCheckoutAsync(payment, payable, [CardMethod], ct);

        if (session is null)
        {
            return PaymentResult<PaymentAttempt>.Fail(PaymentFailure.GatewayUnavailable);
        }

        payment.OpenCheckout(session.SessionId, session.CheckoutUrl, now, CardMethod);
        db.OnlinePayments.Add(payment);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Card checkout {SessionId} opened for {Purpose} {SubjectId}: {Amount} due.",
            session.SessionId,
            purpose,
            subjectId,
            payment.AmountDue);

        return PaymentResult<PaymentAttempt>.Success(Attempt(payment, now));
    }

    /// <summary>
    /// QR Ph, GCash or Maya from our own page: charged the total the customer
    /// was quoted, the gateway's fee in it, so what arrives is what is owed.
    /// </summary>
    private async Task<PaymentResult<PaymentAttempt>> StartIntentAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        string method,
        Payable payable,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var amountDue = payable.VenueAmount + payable.PlatformFee;
        var quote = amountDue > 0m
            ? gateway.QuoteMethods(amountDue).FirstOrDefault(candidate => candidate.Method == method)
            : null;

        if (quote is null)
        {
            return PaymentResult<PaymentAttempt>.Fail(PaymentFailure.UnknownMethod);
        }

        // The same method, still open, for the same money: handed back, not
        // doubled. A second code or a second e-wallet page is a second thing
        // the customer could pay.
        var open = await db.OnlinePayments
            .Where(payment => payment.Purpose == purpose
                && payment.SubjectId == subjectId
                && payment.CustomerUserId == customerUserId
                && payment.Status == OnlinePaymentStatus.Pending
                && payment.ChosenMethod == method
                && payment.PaymentIntentId != null)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (open is not null
            && open.VenueAmount == payable.VenueAmount
            && open.PlatformFee == payable.PlatformFee
            && open.QuotedTotal == quote.Total
            && (open.ExpiresAt is null || open.ExpiresAt > now + QrWorthReusing))
        {
            return PaymentResult<PaymentAttempt>.Success(Attempt(open, now));
        }

        var lifetime = payable.HoldsUntil is DateTimeOffset holdsUntil
            ? TimeSpan.FromTicks(Math.Clamp((holdsUntil - now).Ticks, QrWorthReusing.Ticks, LongestQrLifetime.Ticks))
            : LongestQrLifetime;

        var payment = NewPayment(purpose, subjectId, customerUserId, payable, now);
        PaymentIntentStarted started;

        try
        {
            started = await gateway.StartPaymentIntentAsync(
                new PaymentIntentRequest(
                    method,
                    quote.Total,
                    payable.Description,
                    // Back from the e-wallet whatever happened there. The page
                    // asks us what did; the return itself proves nothing.
                    WithOutcome(payable.ReturnUrl, "returned"),
                    lifetime,
                    Metadata(payment)),
                ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Could not start {Method} for {Purpose} {SubjectId}.", method, purpose, subjectId);

            return PaymentResult<PaymentAttempt>.Fail(PaymentFailure.GatewayUnavailable);
        }

        payment.OpenIntent(
            started.PaymentIntentId,
            method,
            quote.Total,
            started.QrCodeImage,
            started.RedirectUrl,
            started.ExpiresAt,
            now);
        db.OnlinePayments.Add(payment);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "{Method} payment {IntentId} started for {Purpose} {SubjectId}: {Total} charged for {Amount} due.",
            method,
            started.PaymentIntentId,
            purpose,
            subjectId,
            quote.Total,
            amountDue);

        return PaymentResult<PaymentAttempt>.Success(Attempt(payment, now));
    }

    private async Task<WebhookOutcome> CheckoutPaidAsync(GatewayEvent received, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // Kept in the same save as everything it causes. The id is unique, so
        // a second delivery fails that save and changes nothing — and a first
        // delivery that fails halfway leaves no record, so the retry is acted on.
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent(gateway.Provider, received.EventId, received.EventType, now));

        var payment = await db.OnlinePayments
            .FirstOrDefaultAsync(candidate => candidate.CheckoutSessionId == received.CheckoutSessionId, ct);

        if (payment is null)
        {
            logger.LogError(
                "{Provider} reports checkout {SessionId} paid, and there is no payment for it here.",
                gateway.Provider,
                received.CheckoutSessionId);

            return await SaveEventAsync(WebhookOutcome.Ignored, ct);
        }

        return await PaidAsync(payment, received.Payment!, now, ct);
    }

    private async Task<WebhookOutcome> IntentPaidAsync(GatewayEvent received, CancellationToken ct)
    {
        var payment = await db.OnlinePayments
            .FirstOrDefaultAsync(candidate => candidate.PaymentIntentId == received.PaymentIntentId, ct);

        if (payment is null)
        {
            // A hosted checkout's payment raises this event too, beside the
            // checkout's own — and that one is the one acted on. Nothing to do.
            return WebhookOutcome.Ignored;
        }

        var now = timeProvider.GetUtcNow();
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent(gateway.Provider, received.EventId, received.EventType, now));

        return await PaidAsync(payment, received.Payment!, now, ct);
    }

    /// <summary>
    /// Declined, given up on at the e-wallet, or a QR code that ran out. The
    /// attempt is closed so the page can offer another; nothing else changes —
    /// the court is still held until its own time runs out.
    /// </summary>
    private async Task<WebhookOutcome> IntentEndedUnpaidAsync(GatewayEvent received, CancellationToken ct)
    {
        var payment = await db.OnlinePayments
            .FirstOrDefaultAsync(candidate => candidate.PaymentIntentId == received.PaymentIntentId, ct);

        if (payment is null)
        {
            return WebhookOutcome.Ignored;
        }

        var now = timeProvider.GetUtcNow();
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent(gateway.Provider, received.EventId, received.EventType, now));
        payment.EndUnpaid(received.FailureCode ?? received.EventType, now);

        logger.LogInformation(
            "{Method} payment {IntentId} for {Purpose} {SubjectId} ended unpaid: {Reason}.",
            payment.ChosenMethod,
            received.PaymentIntentId,
            payment.Purpose,
            payment.SubjectId,
            received.FailureCode ?? received.EventType);

        return await SaveEventAsync(WebhookOutcome.Accepted, ct);
    }

    private async Task<WebhookOutcome> PaidAsync(
        OnlinePayment payment,
        GatewayPaidPayment paid,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!payment.CanStillBePaid)
        {
            // Already acted on by an earlier event about the same payment.
            return await SaveEventAsync(WebhookOutcome.Accepted, ct);
        }

        var (settlement, handler) = await ApplyPaidAsync(payment, paid, now, ct);

        var outcome = await SaveEventAsync(WebhookOutcome.Accepted, ct);

        if (outcome == WebhookOutcome.Accepted && settlement.Settled && handler is not null)
        {
            await handler.AfterSettledAsync(payment, ct);
        }

        return outcome;
    }

    /// <summary>
    /// Records what the gateway says was paid and settles what it was for, on
    /// the tracked entities, without saving. One path for the webhook and for
    /// asking the gateway directly.
    /// </summary>
    private async Task<(Settlement Settlement, IPaymentPurposeHandler? Handler)> ApplyPaidAsync(
        OnlinePayment payment,
        GatewayPaidPayment paid,
        DateTimeOffset now,
        CancellationToken ct)
    {
        payment.RecordPaid(
            paid.PaymentId,
            paid.PaymentMethod,
            paid.Amount,
            paid.Fee,
            paid.NetAmount,
            paid.PaidAt ?? now,
            now);

        var handler = Handler(payment.Purpose);
        Settlement settlement;

        if (handler is null)
        {
            settlement = Settlement.NeedsAttention(AttentionReason.SubjectNotFound);
        }
        else if (!payment.CoversWhatIsDue)
        {
            settlement = Settlement.NeedsAttention(AttentionReason.AmountShort);
        }
        else
        {
            settlement = await handler.SettleAsync(payment, now, ct);
        }

        if (settlement.Settled)
        {
            payment.Settle(now);
        }
        else
        {
            payment.FlagForAttention(settlement.AttentionReason!, now);

            logger.LogWarning(
                "Payment {PaymentId} for {Purpose} {SubjectId} was paid but needs a person: {Reason}.",
                payment.Id,
                payment.Purpose,
                payment.SubjectId,
                settlement.AttentionReason);
        }

        return (settlement, handler);
    }

    /// <summary>
    /// Saves the event and whatever it changed. A clash on the event's id is a
    /// delivery we have already acted on, which is a success for the gateway.
    /// </summary>
    private async Task<WebhookOutcome> SaveEventAsync(WebhookOutcome outcome, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);

            return outcome;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Settled in the same moment by asking the gateway directly.
            logger.LogInformation("A payment webhook arrived for a payment already settled.");
            db.ChangeTracker.Clear();

            return WebhookOutcome.Accepted;
        }
        catch (DbUpdateException exception) when (IsDuplicateEvent(exception))
        {
            logger.LogInformation("A payment webhook arrived again. Already acted on.");
            db.ChangeTracker.Clear();

            return WebhookOutcome.Accepted;
        }
    }

    /// <summary>
    /// Opens the gateway's checkout for a payment not yet saved. Null when the
    /// gateway could not be reached or refused, which is logged here.
    /// </summary>
    private async Task<CheckoutSessionCreated?> OpenHostedCheckoutAsync(
        OnlinePayment payment,
        Payable payable,
        IReadOnlyCollection<string>? methods,
        CancellationToken ct)
    {
        try
        {
            return await gateway.CreateCheckoutAsync(
                new CheckoutRequest(
                    payable.ReferenceNumber,
                    payable.Description,
                    payable.LineItems,
                    WithOutcome(payable.ReturnUrl, "success"),
                    WithOutcome(payable.ReturnUrl, "cancelled"),
                    Metadata(payment),
                    methods),
                ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(
                exception,
                "Could not open a checkout for {Purpose} {SubjectId}.",
                payment.Purpose,
                payment.SubjectId);

            return null;
        }
    }

    private OnlinePayment NewPayment(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        Payable payable,
        DateTimeOffset now) =>
        new(
            purpose,
            subjectId,
            payable.FacilityOwnerId,
            customerUserId,
            payable.VenueAmount,
            payable.PlatformFee,
            gateway.Provider,
            now,
            payable.FacilityId,
            payable.Description);

    /// <summary>Ours, so a payment on the gateway's dashboard can be traced to a row here without guessing.</summary>
    private static Dictionary<string, string> Metadata(OnlinePayment payment) =>
        new()
        {
            ["payment_id"] = payment.Id.ToString(),
            ["purpose"] = payment.Purpose,
            ["subject_id"] = payment.SubjectId.ToString()
        };

    /// <summary>
    /// The attempt as the page sees it. A QR code past its time is shown as
    /// ended even before the gateway's event says so: the server's clock
    /// decides, so the page never offers a code nobody can pay.
    /// </summary>
    private static PaymentAttempt Attempt(OnlinePayment payment, DateTimeOffset now)
    {
        var ranOut = payment.IsPending && payment.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= now;
        var open = payment.IsPending && !ranOut;

        return new PaymentAttempt(
            payment.Id,
            payment.ChosenMethod ?? string.Empty,
            payment.QuotedTotal ?? payment.AmountDue,
            open ? OnlinePaymentStatus.Pending : OnlinePaymentStatus.Failed,
            open ? payment.QrCodeImage : null,
            open ? payment.NextActionUrl ?? payment.CheckoutUrl : null,
            open && payment.ExpiresAt is DateTimeOffset until ? (int)Math.Max(0, (until - now).TotalSeconds) : null,
            ranOut ? PayMongoGateway.QrPhExpiredEvent : payment.FailureReason);
    }

    /// <summary>SQL Server's unique index violation, which is the only clash this save can have.</summary>
    private static bool IsDuplicateEvent(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private IPaymentPurposeHandler? Handler(string purpose) =>
        handlers.FirstOrDefault(handler => handler.Purpose == purpose);

    private static string WithOutcome(string url, string outcome) =>
        $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}payment={outcome}";
}
