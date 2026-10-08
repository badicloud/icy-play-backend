namespace IcyPlay.Infrastructure.Payments;

/// <summary>
/// The keys and choices for PayMongo. The secret key and the webhook secret
/// are never committed: user-secrets locally, environment variables
/// (<c>PayMongo__SecretKey</c>, <c>PayMongo__WebhookSecret</c>) everywhere else.
/// </summary>
public sealed class PayMongoOptions
{
    public const string SectionName = "PayMongo";

    /// <summary><c>sk_test_…</c> in test mode, <c>sk_live_…</c> live. Server side only.</summary>
    public string SecretKey { get; init; } = string.Empty;

    /// <summary>
    /// The signing secret PayMongo returns when the webhook is created
    /// (<c>whsk_…</c>). Without it no event is believed.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://api.paymongo.com";

    /// <summary>
    /// v2 is the one that passes the gateway's fee on to the customer, which is
    /// the deal: the venue receives exactly what the booking costs.
    /// </summary>
    public string CheckoutPath { get; init; } = "/v2/checkout_sessions";

    /// <summary>
    /// What the checkout offers, in PayMongo's words. Configurable because a
    /// method has to be enabled on the account before PayMongo will offer it,
    /// and test mode and live can differ.
    /// </summary>
    public string[] PaymentMethodTypes { get; init; } = ["qrph", "gcash", "paymaya", "card"];

    /// <summary>
    /// How old a signed event may be before it is turned away as a replay.
    /// Generous, because PayMongo retries and our clock is not theirs.
    /// </summary>
    public int SignatureToleranceSeconds { get; init; } = 600;
}
