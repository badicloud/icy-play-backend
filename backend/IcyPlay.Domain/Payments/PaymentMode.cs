namespace IcyPlay.Domain.Payments;

/// <summary>
/// How a venue is paid. Exactly one of two, and never both.
///
/// A venue that took either would leave every customer choosing between two
/// prices for the same court and every desk checking receipts for bookings
/// that may already have been paid online. One answer per agreement, set by
/// the platform admin on the contract term because a change to it is a change
/// to what was signed.
///
/// Strings rather than an enum, matching <see cref="Bookings.BookingKind"/>.
/// </summary>
public static class PaymentMode
{
    /// <summary>
    /// The customer pays the venue's own GCash and uploads proof; somebody at
    /// the desk looks at it and confirms. The platform fee is billed to the
    /// owner afterwards.
    /// </summary>
    public const string Manual = "Manual";

    /// <summary>
    /// The customer pays through the payment gateway and the gateway's word
    /// confirms it. Nobody at the desk is asked.
    /// </summary>
    public const string Direct = "Direct";

    public static readonly IReadOnlyCollection<string> All = [Manual, Direct];

    public static bool IsSupported(string? value) => value is not null && All.Contains(value);
}
