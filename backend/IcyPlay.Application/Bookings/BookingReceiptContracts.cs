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
    /// <summary>Each online payment against the booking: the first, and any upgrade since.</summary>
    IReadOnlyCollection<ReceiptPayment> Payments,
    /// <summary>The gateway's fees across those payments, which the customer paid on top.</summary>
    decimal ProcessingFeeTotal,
    /// <summary>Everything the customer handed over: the booking, upgrades, and the gateway's fees.</summary>
    decimal AmountPaid,
    DateTimeOffset? ConfirmedAt);

public sealed record ReceiptPayment(
    string Description,
    string? PaymentMethod,
    decimal AmountCharged,
    decimal ProcessingFee,
    DateTimeOffset? PaidAt,
    string? Reference);
