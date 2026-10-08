namespace IcyPlay.Domain.Payments;

/// <summary>
/// What an online payment is for. Each one has its own handler that knows how
/// much is owed and what being paid does to it, so the gateway side is written once.
/// </summary>
public static class PaymentPurpose
{
    /// <summary>A court booking's first payment.</summary>
    public const string Booking = "Booking";

    /// <summary>The difference on a move to dearer hours.</summary>
    public const string BookingUpgrade = "BookingUpgrade";

    /// <summary>A player's place at one open play session.</summary>
    public const string OpenPlayRegistration = "OpenPlayRegistration";

    public static readonly IReadOnlyCollection<string> All = [Booking, BookingUpgrade, OpenPlayRegistration];
}
