namespace IcyPlay.Application.Payments;

/// <summary>One line on the gateway's checkout page.</summary>
public sealed record CheckoutLineItem(string Name, decimal Amount);

/// <summary>
/// What the gateway is asked to collect. Gateway-neutral: amounts in pesos,
/// and the gateway turns them into whatever it counts in.
/// </summary>
public sealed record CheckoutRequest(
    /// <summary>Ours, shown on the gateway's dashboard and exports so a payment can be traced back.</summary>
    string ReferenceNumber,
    string Description,
    IReadOnlyCollection<CheckoutLineItem> LineItems,
    string SuccessUrl,
    string CancelUrl,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record CheckoutSessionCreated(string SessionId, string CheckoutUrl);

/// <summary>A payment the gateway says was made, as read from one of its events.</summary>
public sealed record GatewayPaidPayment(
    string PaymentId,
    string? PaymentMethod,
    decimal Amount,
    decimal Fee,
    decimal NetAmount,
    DateTimeOffset? PaidAt);

/// <summary>
/// One event from the gateway, already checked to be from the gateway.
/// <see cref="Payment"/> is null on events that carry no payment.
/// </summary>
public sealed record GatewayEvent(
    string EventId,
    string EventType,
    bool LiveMode,
    string? CheckoutSessionId,
    GatewayPaidPayment? Payment);

/// <summary>
/// What is owed for one thing, worked out by the handler that owns it.
/// </summary>
public sealed record Payable(
    Guid FacilityOwnerId,
    /// <summary>Which venue, so its desk sees the payment and nobody else's does.</summary>
    Guid FacilityId,
    decimal VenueAmount,
    decimal PlatformFee,
    string ReferenceNumber,
    string Description,
    IReadOnlyCollection<CheckoutLineItem> LineItems,
    /// <summary>Where the customer comes back to, with the outcome appended by the service.</summary>
    string ReturnUrl);

public sealed record CheckoutStarted(Guid PaymentId, string CheckoutUrl);

public enum PaymentFailure
{
    None,
    /// <summary>Nothing of that purpose and id belongs to this customer.</summary>
    NotFound,
    /// <summary>The venue takes GCash receipts, so there is nothing to pay online.</summary>
    NotPaidOnline,
    /// <summary>Already paid, cancelled, refused or otherwise not waiting for money.</summary>
    NotAwaitingPayment,
    /// <summary>The hold ran out; the hours are back on sale.</summary>
    HoldExpired,
    /// <summary>No handler for that purpose.</summary>
    UnknownPurpose,
    /// <summary>The gateway is not set up on this server.</summary>
    GatewayNotConfigured,
    /// <summary>The gateway turned the request down or could not be reached.</summary>
    GatewayUnavailable
}

public sealed record PaymentResult<T>(T? Value, PaymentFailure Failure = PaymentFailure.None)
{
    public bool Succeeded => Failure == PaymentFailure.None;
    public static PaymentResult<T> Success(T value) => new(value);
    public static PaymentResult<T> Fail(PaymentFailure failure) => new(default, failure);
}

/// <summary>What became of what a paid payment was paying for.</summary>
public sealed record Settlement(bool Settled, string? AttentionReason)
{
    public static Settlement Done { get; } = new(true, null);
    public static Settlement NeedsAttention(string reason) => new(false, reason);
}

public enum WebhookOutcome
{
    /// <summary>Read and acted on, or recognised as already acted on.</summary>
    Accepted,
    /// <summary>Not signed by the gateway. Nothing was done.</summary>
    Rejected,
    /// <summary>Genuine, but nothing we act on.</summary>
    Ignored
}
