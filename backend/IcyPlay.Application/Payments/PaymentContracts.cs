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
    IReadOnlyDictionary<string, string> Metadata,
    /// <summary>
    /// The methods the page offers, when not every one the gateway is set up
    /// for: a card alone, when the other methods are paid on our own page.
    /// </summary>
    IReadOnlyCollection<string>? PaymentMethodTypes = null);

public sealed record CheckoutSessionCreated(string SessionId, string CheckoutUrl);

/// <summary>
/// One way to pay, and what it comes to: what is owed, the gateway's fee on
/// top, VAT included, and the total the customer is charged.
/// </summary>
public sealed record PaymentMethodQuote(string Method, decimal AmountDue, decimal ProcessingFee, decimal Total);

/// <summary>
/// One way of paying, started on our own page rather than the gateway's: a
/// single method and a single amount, the gateway's fee already in it.
/// </summary>
public sealed record PaymentIntentRequest(
    /// <summary>The gateway's word for the method: qrph, gcash, paymaya.</summary>
    string Method,
    /// <summary>What the customer is charged, the fee included.</summary>
    decimal Amount,
    string Description,
    /// <summary>Where an e-wallet sends the customer back to, whether they paid or not.</summary>
    string ReturnUrl,
    /// <summary>How long a QR code may be scanned for. Ignored by the other methods.</summary>
    TimeSpan QrLifetime,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// What the customer does next: scan <see cref="QrCodeImage"/> (a data URL),
/// or be sent to <see cref="RedirectUrl"/> to approve in their e-wallet. A
/// QR code in test mode carries the gateway's page that pretends to pay it as
/// its <see cref="RedirectUrl"/>; a live one has none.
/// </summary>
public sealed record PaymentIntentStarted(
    string PaymentIntentId,
    string? RedirectUrl,
    string? QrCodeImage,
    /// <summary>When a QR code stops working. Null for the others.</summary>
    DateTimeOffset? ExpiresAt);

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
/// <see cref="Payment"/> is null on events that carry no paid payment. An
/// event is about a hosted checkout (<see cref="CheckoutSessionId"/>) or a
/// payment started on our own page (<see cref="PaymentIntentId"/>).
/// </summary>
public sealed record GatewayEvent(
    string EventId,
    string EventType,
    bool LiveMode,
    string? CheckoutSessionId,
    GatewayPaidPayment? Payment,
    string? PaymentIntentId = null,
    /// <summary>The gateway's reason, on an event saying a payment failed.</summary>
    string? FailureCode = null);

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
    string ReturnUrl,
    /// <summary>
    /// When the hold on it runs out, so a QR code is not offered for longer
    /// than the court is kept. Null when nothing is held.
    /// </summary>
    DateTimeOffset? HoldsUntil = null);

public sealed record CheckoutStarted(Guid PaymentId, string CheckoutUrl);

/// <summary>
/// What the second step offers when the customer pays on our own page: each
/// method and its total, and the attempt already under way, if any.
/// </summary>
public sealed record PaymentOptions(
    /// <summary>One of <see cref="CheckoutExperience"/>.</summary>
    string Experience,
    decimal AmountDue,
    /// <summary>The methods paid on our page, cheapest first as the gateway is set up.</summary>
    IReadOnlyList<PaymentMethodQuote> Methods,
    /// <summary>The latest attempt on our page, still open or just ended unpaid.</summary>
    PaymentAttempt? Current);

/// <summary>
/// One way of paying under way: a QR code to scan, or a page to go to — the
/// e-wallet's, or the gateway's card page. Its time is counted by the server,
/// never the browser.
/// </summary>
public sealed record PaymentAttempt(
    Guid PaymentId,
    string Method,
    decimal Total,
    /// <summary><c>Pending</c> while it can be paid; <c>Failed</c> once it cannot.</summary>
    string Status,
    string? QrCodeImage,
    string? RedirectUrl,
    /// <summary>Seconds until a QR code stops working, by the server's clock. Null for the others.</summary>
    int? SecondsLeft,
    string? FailureReason);

/// <summary>The method the customer picked on the second step.</summary>
public sealed record StartPaymentRequest(string Method);

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
    GatewayUnavailable,
    /// <summary>A way of paying our page does not offer.</summary>
    UnknownMethod
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
