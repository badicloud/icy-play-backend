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
        // on, and a customer who pays both has paid twice.
        var open = await db.OnlinePayments
            .Where(payment => payment.Purpose == purpose
                && payment.SubjectId == subjectId
                && payment.Status == OnlinePaymentStatus.Pending
                && payment.CheckoutUrl != null)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (open is not null && open.VenueAmount == payable.VenueAmount && open.PlatformFee == payable.PlatformFee)
        {
            return PaymentResult<CheckoutStarted>.Success(new CheckoutStarted(open.Id, open.CheckoutUrl!));
        }

        var now = timeProvider.GetUtcNow();
        var payment = new OnlinePayment(
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

        CheckoutSessionCreated session;

        try
        {
            session = await gateway.CreateCheckoutAsync(
                new CheckoutRequest(
                    payable.ReferenceNumber,
                    payable.Description,
                    payable.LineItems,
                    WithOutcome(payable.ReturnUrl, "success"),
                    WithOutcome(payable.ReturnUrl, "cancelled"),
                    new Dictionary<string, string>
                    {
                        // Ours, so a payment on the gateway's dashboard can be
                        // traced to a row here without guessing.
                        ["payment_id"] = payment.Id.ToString(),
                        ["purpose"] = purpose,
                        ["subject_id"] = subjectId.ToString()
                    }),
                ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogError(exception, "Could not open a checkout for {Purpose} {SubjectId}.", purpose, subjectId);

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

    public async Task<WebhookOutcome> HandleWebhookAsync(string rawBody, string? signatureHeader, CancellationToken ct)
    {
        var received = gateway.ReadEvent(rawBody, signatureHeader);

        if (received is null)
        {
            logger.LogWarning("A payment webhook arrived that was not signed by {Provider}. Ignored.", gateway.Provider);

            return WebhookOutcome.Rejected;
        }

        if (received.EventType != PayMongoGateway.CheckoutPaidEvent ||
            received.Payment is null ||
            string.IsNullOrWhiteSpace(received.CheckoutSessionId))
        {
            return WebhookOutcome.Ignored;
        }

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

        if (!payment.IsPending)
        {
            // Already acted on by an earlier event about the same checkout.
            return await SaveEventAsync(WebhookOutcome.Accepted, ct);
        }

        var (settlement, handler) = await ApplyPaidAsync(payment, received.Payment, now, ct);

        var outcome = await SaveEventAsync(WebhookOutcome.Accepted, ct);

        if (outcome == WebhookOutcome.Accepted && settlement.Settled && handler is not null)
        {
            await handler.AfterSettledAsync(payment, ct);
        }

        return outcome;
    }

    /// <summary>
    /// Asks the gateway directly whether a checkout this customer opened has
    /// been paid, and settles it if so. The webhook is the usual way to hear;
    /// this is for when it has not arrived — a network fault, a restart, a
    /// tunnel gone — and the customer is sitting on "Confirming your payment".
    /// The same settlement as the webhook's, so whichever lands first wins and
    /// the other finds nothing left to do.
    /// </summary>
    public async Task<bool> VerifyAsync(string purpose, Guid subjectId, Guid customerUserId, CancellationToken ct)
    {
        var payment = await db.OnlinePayments
            .Where(candidate => candidate.Purpose == purpose
                && candidate.SubjectId == subjectId
                && candidate.CustomerUserId == customerUserId
                && candidate.Status == OnlinePaymentStatus.Pending
                && candidate.CheckoutSessionId != null)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (payment is null || !gateway.IsConfigured)
        {
            return false;
        }

        GatewayPaidPayment? paid;

        try
        {
            paid = await gateway.GetPaidPaymentAsync(payment.CheckoutSessionId!, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not ask {Provider} about checkout {SessionId}.", gateway.Provider, payment.CheckoutSessionId);

            return false;
        }

        if (paid is null)
        {
            return false;
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
            "Checkout {SessionId} found paid by asking {Provider}, not by webhook.",
            payment.CheckoutSessionId,
            gateway.Provider);

        if (settlement.Settled && handler is not null)
        {
            await handler.AfterSettledAsync(payment, ct);
        }

        return true;
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

    /// <summary>SQL Server's unique index violation, which is the only clash this save can have.</summary>
    private static bool IsDuplicateEvent(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private IPaymentPurposeHandler? Handler(string purpose) =>
        handlers.FirstOrDefault(handler => handler.Purpose == purpose);

    private static string WithOutcome(string url, string outcome) =>
        $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}payment={outcome}";
}
