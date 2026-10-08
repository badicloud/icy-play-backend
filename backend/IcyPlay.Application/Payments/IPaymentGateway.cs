namespace IcyPlay.Application.Payments;

/// <summary>
/// The payment gateway, behind one seam. PayMongo today; the rest of the system
/// only knows that a checkout can be opened and that events come back signed.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The gateway's name, as stored on each payment.</summary>
    string Provider
    {
        get;
    }

    /// <summary>
    /// Whether this server has the key to open checkouts. Reading events needs
    /// the webhook's signing secret as well, which <see cref="ReadEvent"/>
    /// checks for itself: a checkout can be opened, and paid, before the
    /// webhook exists — the payment then waits for it rather than being lost.
    /// </summary>
    bool IsConfigured
    {
        get;
    }

    Task<CheckoutSessionCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);

    /// <summary>
    /// Asks the gateway whether a checkout has been paid, for when its webhook
    /// has not arrived. Null while nothing has been paid on it.
    /// </summary>
    Task<GatewayPaidPayment?> GetPaidPaymentAsync(string checkoutSessionId, CancellationToken ct);

    /// <summary>
    /// Reads an event the gateway posted, from the exact bytes it sent.
    /// Null when the signature does not check out: whatever it says, it did not
    /// come from the gateway.
    /// </summary>
    GatewayEvent? ReadEvent(string rawBody, string? signatureHeader);
}
