using IcyPlay.Domain.Bookings;

namespace IcyPlay.Domain.Payments;

/// <summary>
/// How long something paid through the gateway keeps its court.
///
/// Longer than the hold on a GCash receipt, because the customer leaves the
/// site to pay: a gateway page, a bank's app, an OTP by text, then back. A
/// dropped connection anywhere along that is ordinary on a phone, and losing
/// a court to it while the money is already on its way is the complaint this
/// is here to prevent.
///
/// The same floor and ceiling as <see cref="PaymentHold"/>: the reasons for
/// those do not change with how the money moves.
/// </summary>
public static class OnlineHold
{
    public const int DefaultMinutes = 15;

    public static int Clamp(int minutes) => PaymentHold.Clamp(minutes);

    public static bool IsSupported(int minutes) => PaymentHold.IsSupported(minutes);
}
