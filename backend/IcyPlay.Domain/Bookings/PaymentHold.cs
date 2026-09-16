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
    /// What a venue holds a court for while waiting to be paid, unless it says
    /// otherwise.
    ///
    /// It was half an hour, on the reasoning that somebody should be able to
    /// open GCash and pay without hurrying. Venues came back and said half an
    /// hour is a court sitting dark on a Saturday because somebody wandered
    /// off, and that paying takes a minute or two. Their floor, their call.
    /// </summary>
    public const int DefaultMinutes = 5;

    /// <summary>
    /// Under five minutes a customer is racing the clock rather than paying,
    /// and every abandoned payment is a complaint. A day is not a hold either,
    /// it is a free reservation, and the whole point is that it is neither.
    /// </summary>
    public const int MinimumMinutes = 5;
    public const int MaximumMinutes = 240;

    public static int Clamp(int minutes) =>
        Math.Clamp(minutes, MinimumMinutes, MaximumMinutes);

    public static bool IsSupported(int minutes) =>
        minutes >= MinimumMinutes && minutes <= MaximumMinutes;
}
