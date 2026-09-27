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
    /// The same diary and the same list, for a platform admin.
    ///
    /// They do not work at a venue, so the desk's own gate — does this person
    /// attend this court — answers no for every court on the platform. They
    /// still have to be able to look: the court inventory is theirs to police,
    /// and "who is on this court" is the question behind closing one for
    /// maintenance.
    ///
    /// Read only. Confirming a payment and approving an upgrade are the
    /// venue's to do, and they stay behind the desk's door. These two are the
    /// whole of what the platform may see, which is why they are named
    /// separately rather than hidden behind a flag on the pair above: a
    /// parameter that widens who may call something is a parameter somebody
    /// passes by accident.
    /// </summary>
    Task<DeskResult<IReadOnlyCollection<ScheduleEntry>>> ScheduleForPlatformAsync(
        Guid courtId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);

    /// <inheritdoc cref="ScheduleForPlatformAsync" />
    Task<DeskResult<PagedResult<DeskBooking>>> CourtBookingsForPlatformAsync(
        CourtBookingQuery query,
        CancellationToken ct);

    /// <summary>
    /// One booking in full, and its account of itself, for a platform admin.
    ///
    /// Both pages above lead here — an hour on the diary is clicked to see
    /// whose it is, a row in the list opens its own history — so stopping at
    /// the court would leave a page whose every link is a refusal.
    /// </summary>
    Task<DeskResult<DeskBooking>> BookingForPlatformAsync(Guid bookingId, CancellationToken ct);

    /// <inheritdoc cref="BookingForPlatformAsync" />
    Task<DeskResult<IReadOnlyCollection<BookingHistoryEntry>>> HistoryForPlatformAsync(
        Guid bookingId,
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

    /// <summary>
    /// How much of what this person's venues had open actually got used, court
    /// by court and part by part.
    ///
    /// The money in it is the owner's: an attendant's copy comes back with the
    /// rental left out rather than hidden, because a figure the page does not
    /// draw is still a figure in the response.
    /// </summary>
    Task<DeskResult<UtilizationReport>> UtilizationAsync(
        Guid userId,
        UtilizationQuery query,
        CancellationToken ct);

    /// <summary>
    /// The utilization figures cut by date rather than totalled per court:
    /// the line beside the total.
    ///
    /// No money in it, so an attendant sees what an owner sees. Hours are the
    /// desk's own business.
    /// </summary>
    Task<DeskResult<HoursOverTime>> HoursOverTimeAsync(
        Guid userId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>
    /// How many bookings customers moved in a range, period by period, and the
    /// reasons they gave.
    ///
    /// No money in it, so an attendant sees what an owner sees: a customer's
    /// reason is already in the booking history the desk reads.
    /// </summary>
    Task<DeskResult<MovesReport>> MovesAsync(
        Guid userId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>
    /// What the venue looks like at this moment: how many courts there are, and
    /// how many of them have somebody on them.
    ///
    /// No money in it at all, so an attendant sees the same answer an owner
    /// does. Counting courts is the desk's own job.
    /// </summary>
    Task<DeskResult<VenueSnapshot>> SnapshotAsync(
        Guid userId,
        Guid? facilityId,
        CancellationToken ct);

    /// <summary>
    /// The attendants at the venues this person owns, and whether each may read
    /// the money. Owners only: an attendant asking is refused.
    /// </summary>
    Task<DeskResult<IReadOnlyCollection<DeskAttendant>>> AttendantsAsync(Guid userId, CancellationToken ct);

    /// <summary>Lets one attendant read the venue's money, or stops them. Owners only.</summary>
    Task<DeskResult<DeskAttendant>> SetAttendantMoneyAsync(
        Guid userId,
        Guid attendantId,
        bool canSeeMoney,
        AuditActor actor,
        CancellationToken ct);

    Task<DeskResult<DeskSettings>> UpdateSettingsAsync(
        Guid userId,
        UpdateDeskSettingsRequest request,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// Turns a payment down, with a reason from the list. The hours go back on
    /// sale, and the reason is what the customer reads on their booking.
    /// </summary>
    Task<DeskResult<DeskBooking>> RejectAsync(
        Guid userId,
        Guid bookingId,
        RejectBookingRequest request,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// What this person's venues have right now — courts by venue type, and the
    /// sports and events each is set up for — with how much each venue type
    /// sold in the range.
    ///
    /// Open to attendants as well as owners for now.
    /// </summary>
    Task<DeskResult<CourtMixReport>> CourtMixAsync(
        Guid userId,
        CourtMixQuery query,
        CancellationToken ct);

    /// <summary>
    /// Every change made to this person's courts in a range, newest first,
    /// read back out of the audit trail.
    ///
    /// Open to attendants as well as owners for now; report visibility is a
    /// permission still to be built.
    /// </summary>
    Task<DeskResult<CourtChangesReport>> CourtChangesAsync(
        Guid userId,
        CourtChangesQuery query,
        CancellationToken ct);

    /// <summary>
    /// What the venue's open, unsold hours would have earned, period by period
    /// and court by court, down to each sport court.
    ///
    /// Open to attendants as well as owners for now; who may see money is a
    /// permission still to be built.
    /// </summary>
    Task<DeskResult<MissedReport>> MissedAsync(
        Guid userId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>
    /// What customers paid the venue in a range, period by period and court by
    /// court, each on the day the desk confirmed or approved it.
    ///
    /// Open to attendants as well as owners for now; who may see money is a
    /// permission still to be built.
    /// </summary>
    Task<DeskResult<TakingsReport>> TakingsAsync(
        Guid userId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>
    /// How many payments the desk turned down in a range, period by period,
    /// against how many it checked, and why.
    /// </summary>
    Task<DeskResult<DeclinesReport>> DeclinesAsync(
        Guid userId,
        HoursQuery query,
        CancellationToken ct);
}
