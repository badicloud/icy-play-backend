using IcyPlay.Application.Audit;
using IcyPlay.Application.Common;

namespace IcyPlay.Application.Bookings;

/// <summary>
/// The venue's side of a booking: the queue of payments waiting to be checked,
/// and the two answers somebody at the desk can give.
///
/// Scoped to the person, not to a facility they name. An owner works every venue
/// they own and an attendant works the ones they are on, so the caller does not
/// get to say which desk they are standing at.
/// </summary>
public interface IDeskService
{
    /// <summary>
    /// The venues this person may confirm bookings for. Empty when they work
    /// none, which is worth showing plainly rather than as an empty queue.
    /// </summary>
    Task<IReadOnlyCollection<DeskVenue>> VenuesAsync(Guid userId, CancellationToken ct);

    Task<DeskResult<PagedResult<DeskBooking>>> ListAsync(
        Guid userId,
        DeskQuery query,
        CancellationToken ct);

    /// <summary>
    /// The courts this person's venues have registered, each with the parts it
    /// is sold in. What the diary is organised by.
    /// </summary>
    Task<IReadOnlyCollection<DeskCourt>> CourtsAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Every booked hour on one court between two dates, thin enough to draw a
    /// month of. Only what still stands: a lapsed hold and a rejected payment
    /// hold nothing, and an hour drawn as taken that anybody can book is worse
    /// than an empty square.
    /// </summary>
    Task<DeskResult<IReadOnlyCollection<ScheduleEntry>>> ScheduleAsync(
        Guid userId,
        Guid courtId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    /// <summary>
    /// One court's bookings as a list, a page at a time. Unlike the diary this
    /// will answer for what fell through, because that is what a venue comes to
    /// a list to find.
    /// </summary>
    Task<DeskResult<PagedResult<DeskBooking>>> CourtBookingsAsync(
        Guid userId,
        CourtBookingQuery query,
        CancellationToken ct);

    /// <summary>
    /// Everything that has happened to one booking, newest first.
    ///
    /// The same account the customer reads, because it is the same booking. A
    /// desk that could not see a move or an upgrade would be working from a
    /// diary that changed without explanation.
    /// </summary>
    Task<DeskResult<IReadOnlyCollection<BookingHistoryEntry>>> HistoryAsync(
        Guid userId,
        Guid bookingId,
        CancellationToken cancellationToken);

    /// <summary>One booking in full, for an hour somebody has clicked.</summary>
    Task<DeskResult<DeskBooking>> BookingAsync(
        Guid userId,
        Guid bookingId,
        CancellationToken ct);

    /// <summary>
    /// Says the payment is good. The customer is told; it is the one letter
    /// they get that reads as a confirmation.
    /// </summary>
    Task<DeskResult<DeskBooking>> ConfirmAsync(
        Guid userId,
        Guid bookingId,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// Says it is not, with a reason. The hours go back on sale, because a
    /// rejected booking holds nothing.
    /// </summary>
    /// <summary>
    /// Upgrades customers have paid for and handed over, and the ones already
    /// settled.
    ///
    /// Its own queue rather than a row in the booking queue: an upgrade is a
    /// different decision. A booking confirmation asks whether a payment is
    /// real; this asks that and whether a particular court is free, and the
    /// two need different things on screen.
    /// </summary>
    Task<DeskResult<PagedResult<DeskUpgrade>>> UpgradesAsync(
        Guid userId,
        DeskUpgradeQuery query,
        CancellationToken ct);

    /// <summary>
    /// Says the payment is good and moves the booking onto the better court.
    ///
    /// This is the only place a booking moves without the customer asking,
    /// because it is the one they already asked for and paid for. The hours
    /// they leave go back on sale the moment it lands.
    /// </summary>
    Task<DeskResult<DeskUpgrade>> ApproveUpgradeAsync(
        Guid userId,
        Guid upgradeId,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// Says no, with a reason the customer is shown. The booking stays exactly
    /// where it was.
    /// </summary>
    Task<DeskResult<DeskUpgrade>> DeclineUpgradeAsync(
        Guid userId,
        Guid upgradeId,
        string? reason,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// What this venue has set for itself, with the range each dial allows so
    /// the panel can say what is possible rather than refusing after the fact.
    /// </summary>
    Task<DeskResult<DeskSettings>> SettingsAsync(Guid userId, CancellationToken ct);

    Task<DeskResult<DeskSettings>> UpdateSettingsAsync(
        Guid userId,
        UpdateDeskSettingsRequest request,
        AuditActor actor,
        CancellationToken ct);

    Task<DeskResult<DeskBooking>> RejectAsync(
        Guid userId,
        Guid bookingId,
        string? reason,
        AuditActor actor,
        CancellationToken ct);
}
