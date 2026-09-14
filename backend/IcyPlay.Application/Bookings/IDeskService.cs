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
    Task<DeskResult<DeskBooking>> RejectAsync(
        Guid userId,
        Guid bookingId,
        string? reason,
        AuditActor actor,
        CancellationToken ct);
}
