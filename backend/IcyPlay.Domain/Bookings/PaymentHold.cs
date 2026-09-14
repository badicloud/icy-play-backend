namespace IcyPlay.Domain.Bookings;

/// <summary>
/// How long an unpaid booking keeps its court.
///
/// A hold costs nothing to make, so without a clock on it one account could sit
/// on every hour of every court and never pay. The clock is what makes a
/// booking an offer rather than a claim.
/// </summary>
public static class PaymentHold
{
    /// <summary>
    /// Long enough to open GCash, pay, and photograph the receipt without
    /// hurrying; short enough that a court abandoned mid-payment is back on
    /// sale the same evening.
    /// </summary>
    public const int DefaultMinutes = 30;

    /// <summary>
    /// Five minutes is not enough time to pay for anything. A day is not a hold,
    /// it is a free reservation, and the whole point is that it is neither.
    /// </summary>
    public const int MinimumMinutes = 5;
    public const int MaximumMinutes = 240;

    public static int Clamp(int minutes) =>
        Math.Clamp(minutes, MinimumMinutes, MaximumMinutes);

    public static bool IsSupported(int minutes) =>
        minutes >= MinimumMinutes && minutes <= MaximumMinutes;
}
