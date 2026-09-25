using IcyPlay.Domain.Bookings;

namespace IcyPlay.Application.Bookings;

/// <summary>
/// Tells people a booking has moved. Its own service rather than inline in the
/// booking service, because sending mail fails in ways taking a booking does
/// not — a mail provider being down must not look like a court not being held.
/// </summary>
/// <summary>
/// What a booking looked like before it moved, and what it looks like now.
///
/// Both said in words rather than handed over as slots, because the caller is
/// the one that owns how a booking's hours are phrased, and two places
/// phrasing the same thing drift apart.
/// </summary>
public sealed record BookingMoveNotice(string FromCourtName, string WasWhen, string NowWhen);

public interface IBookingNotifier
{
    /// <summary>
    /// Two letters when a receipt arrives: the customer is told their court is
    /// held while the venue checks it, and the venue is told somebody is waiting
    /// on them.
    ///
    /// The customer's letter is careful not to read as a confirmation. Nothing
    /// is confirmed until a person has looked at the payment, and a customer who
    /// turns up on the strength of the wrong email finds somebody else on the
    /// court.
    /// </summary>
    Task PaymentSubmittedAsync(Booking booking, CancellationToken ct);

    /// <summary>
    /// Two letters when an upgrade's receipt arrives: the customer is told the
    /// venue is checking it, and the venue is told somebody is waiting.
    ///
    /// The customer's is careful in the same way the booking's is, and for a
    /// sharper reason. Their booking has NOT moved, and somebody who reads it
    /// as a confirmation turns up at the wrong court — one they can see is not
    /// theirs, while the one that is sits taken.
    /// </summary>
    Task UpgradeSubmittedAsync(BookingUpgradeRequest upgrade, CancellationToken ct);

    /// <summary>
    /// Tells the customer the venue said yes and the booking has moved. The one
    /// letter in this flow that is a confirmation, and the only one that can
    /// safely say which court to walk to.
    /// </summary>
    Task UpgradeApprovedAsync(BookingUpgradeRequest upgrade, CancellationToken ct);

    /// <summary>
    /// Tells the venue a booking has moved itself.
    ///
    /// A free move needs nobody's permission and takes effect at once, so the
    /// desk's diary changed without anybody at the desk doing anything. This
    /// is the only thing that tells them.
    ///
    /// The before state is passed in because it no longer exists: a booking
    /// only ever knows where it is now, and by the time this is called it has
    /// already moved.
    /// </summary>
    Task BookingMovedAsync(Booking booking, BookingMoveNotice notice, CancellationToken ct);

    /// <summary>
    /// Thanks the customer and confirms their court. The one letter they get,
    /// sent when a person at the venue has actually checked the payment.
    /// </summary>
    Task BookingConfirmedAsync(Booking booking, CancellationToken ct);

    /// <summary>
    /// Tells the customer the venue turned their payment down: that the court
    /// is not held, the reason the desk gave, and who to speak to about any
    /// money they sent — the venue, because IcyPlay never holds it.
    /// </summary>
    Task BookingDeclinedAsync(Booking booking, CancellationToken ct);
}
