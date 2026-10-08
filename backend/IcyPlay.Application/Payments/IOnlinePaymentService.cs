namespace IcyPlay.Application.Payments;

public interface IOnlinePaymentService
{
    /// <summary>
    /// Opens a checkout for something the customer owes, or hands back the one
    /// already open for it. One open checkout at a time: two would let the
    /// customer pay twice.
    /// </summary>
    Task<PaymentResult<CheckoutStarted>> StartCheckoutAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        CancellationToken ct);

    /// <summary>
    /// Asks the gateway directly whether this customer's open checkout for the
    /// thing has been paid, and settles it if so. True when it has been.
    /// </summary>
    Task<bool> VerifyAsync(string purpose, Guid subjectId, Guid customerUserId, CancellationToken ct);

    /// <summary>Acts on an event the gateway posted.</summary>
    Task<WebhookOutcome> HandleWebhookAsync(string rawBody, string? signatureHeader, CancellationToken ct);
}
