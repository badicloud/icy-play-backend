namespace IcyPlay.Application.Payments;

/// <summary>
/// One payment made through the gateway at a venue, as its desk reads it.
/// Money throughout, so only people who may see the venue's money are shown it.
/// </summary>
public sealed record DeskTransaction(
    Guid Id,
    Guid FacilityId,
    string FacilityName,
    /// <summary><c>Booking</c>, <c>BookingUpgrade</c> or <c>OpenPlayRegistration</c>.</summary>
    string Purpose,
    /// <summary>What was paid for, as the customer saw it at the gateway.</summary>
    string? Description,
    string CustomerName,
    string CustomerEmail,
    /// <summary>gcash, qrph, card, paymaya — in the gateway's words.</summary>
    string? PaymentMethod,
    /// <summary>What the customer was charged, the gateway's fee included.</summary>
    decimal? AmountCharged,
    decimal? ProcessingFee,
    /// <summary>What arrived after the fee: the venue's share and the platform fee.</summary>
    decimal? NetAmount,
    decimal VenueAmount,
    decimal PlatformFee,
    /// <summary>The gateway's payment id, to find it on the gateway's own dashboard.</summary>
    string? Reference,
    /// <summary><c>Paid</c>, or <c>NeedsAttention</c> when it could not settle anything by itself.</summary>
    string Status,
    string? AttentionReason,
    DateTimeOffset? PaidAt,
    /// <summary>The booking it belongs to, for a link to it. Null on an open play.</summary>
    Guid? BookingId,
    /// <summary>Arrived since this person last opened the list.</summary>
    bool IsNew);

/// <summary>What the badge on the desk says about online payments.</summary>
public sealed record DeskTransactionSummary(
    /// <summary>Arrived since this person last opened the list.</summary>
    int Unseen,
    /// <summary>Paid but waiting on a person, however long ago. Not cleared by looking.</summary>
    int NeedsAttention);
