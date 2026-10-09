namespace IcyPlay.Application.Bookings;

/// <summary>
/// What a customer paid for a booking, line by line, including the payment
/// gateway's fee that its own receipt shows only as part of a total.
///
/// Not an official receipt: the venue sold the court, and an official receipt
/// for it is the venue's to issue. This is IcyPlay's account of what was paid,
/// so the figures can be checked.
/// </summary>
public sealed record BookingReceipt(
    /// <summary>The same reference the gateway shows, so the two can be matched.</summary>
    string ReceiptNumber,
    Guid BookingId,
    string CustomerName,
    string CustomerEmail,
    string CourtName,
    string FacilityName,
    string SportName,
    string? ContactPhone,
    string? ContactEmail,
    IReadOnlyCollection<BookedSlot> Slots,
    decimal RentalTotal,
    decimal PlatformFeeTotal,
    /// <summary>Court rental and platform fee: what the booking itself costs.</summary>
    decimal BookingTotal,
    /// <summary><c>Manual</c> (GCash receipt checked by the venue) or <c>Direct</c> (paid online).</summary>
    string PaymentChannel,
    /// <summary>The booking's own online payment. An upgrade has a receipt of its own.</summary>
    IReadOnlyCollection<ReceiptPayment> Payments,
    /// <summary>The gateway's fee on that payment, which the customer paid on top.</summary>
    decimal ProcessingFeeTotal,
    /// <summary>What the customer handed over for the booking itself, the gateway's fee included.</summary>
    decimal AmountPaid,
    DateTimeOffset? ConfirmedAt,
    /// <summary>
    /// Set when the booking has moved since it was paid for: the hours as
    /// first bought, in a sentence, in place of <see cref="Slots"/> — which
    /// would be the hours now, on a court at a price this payment was not for.
    /// </summary>
    string? OriginalSummary = null,
    /// <summary>Upgrades paid for since, each with a receipt of its own.</summary>
    IReadOnlyCollection<UpgradeReceiptSummary>? Upgrades = null);

/// <summary>An upgrade paid for on a booking, named so its own receipt can be found.</summary>
public sealed record UpgradeReceiptSummary(
    string ReceiptNumber,
    string? FromCourtName,
    string ToCourtName,
    decimal AmountPaid,
    DateTimeOffset? PaidAt);

public sealed record ReceiptPayment(
    string Description,
    string? PaymentMethod,
    decimal AmountCharged,
    decimal ProcessingFee,
    DateTimeOffset? PaidAt,
    string? Reference);
