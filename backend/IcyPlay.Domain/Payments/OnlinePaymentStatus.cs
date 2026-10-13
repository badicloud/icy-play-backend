namespace IcyPlay.Domain.Payments;

public static class OnlinePaymentStatus
{
    /// <summary>A checkout has been opened and nothing has come back from it yet.</summary>
    public const string Pending = "Pending";

    /// <summary>The gateway says it was paid, and what it paid for has been settled.</summary>
    public const string Paid = "Paid";

    /// <summary>
    /// The gateway says it was paid, but what it paid for could not be settled
    /// by itself: the hold ran out first, the thing was already paid for, or
    /// the amount does not add up. The money is real, so this is never dropped
    /// and never refunded automatically — the venue decides.
    /// </summary>
    public const string NeedsAttention = "NeedsAttention";

    /// <summary>
    /// A way of paying started on our own page ended with nothing paid: the
    /// e-wallet declined it or the customer gave up there, or the QR code ran
    /// out. Nothing is owed back. The customer can start another.
    /// </summary>
    public const string Failed = "Failed";
}
