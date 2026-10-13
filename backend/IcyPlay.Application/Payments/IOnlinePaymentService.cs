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
    /// What the second step offers for this thing: each method our page takes,
    /// its total with the gateway's fee, and the attempt already under way.
    /// </summary>
    Task<PaymentResult<PaymentOptions>> GetOptionsAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        CancellationToken ct);

    /// <summary>
    /// Starts paying by one method on our page: a QR code to show, the
    /// e-wallet's page, or the gateway's page for a card alone. The same
    /// method asked again, for the same money, hands back the attempt
    /// already open rather than a second one to pay.
    /// </summary>
    Task<PaymentResult<PaymentAttempt>> StartMethodAsync(
        string purpose,
        Guid subjectId,
        Guid customerUserId,
        string method,
        CancellationToken ct);

    /// <summary>
    /// Asks the gateway directly whether any attempt this customer opened for
    /// the thing has been paid — at a hosted checkout or on our own page — and
    /// settles it if so. True when it has been.
    /// </summary>
    Task<bool> VerifyAsync(string purpose, Guid subjectId, Guid customerUserId, CancellationToken ct);

    /// <summary>Acts on an event the gateway posted.</summary>
    Task<WebhookOutcome> HandleWebhookAsync(string rawBody, string? signatureHeader, CancellationToken ct);
}
