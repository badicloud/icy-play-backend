namespace IcyPlay.Application.Bookings;

public interface IBookingService
{
    /// <summary>
    /// What can be booked on one court on one date. Anonymous: a visitor should
    /// be able to see whether Saturday morning is free before being asked to
    /// sign in.
    /// </summary>
    Task<BookingResult<AvailabilityDay>> AvailabilityAsync(
        Guid bookableCourtId,
        DateOnly date,
        CancellationToken ct);

    /// <summary>
    /// Every date in the booking window, counted rather than priced, so a day
    /// picker can grey out the days that cannot be taken whole before anybody
    /// clicks one.
    /// </summary>
    Task<BookingResult<IReadOnlyCollection<DayOutlook>>> OutlookAsync(
        Guid bookableCourtId,
        CancellationToken ct);

    /// <summary>
    /// Takes the hours, or says why it could not. Prices every hour on the
    /// server: what the customer was shown is a quote, and the only number that
    /// binds anyone is the one written here.
    /// </summary>
    Task<BookingResult<BookingDetail>> CreateAsync(
        CreateBookingRequest request,
        Guid customerUserId,
        CancellationToken ct);

    /// <summary>One booking, for the customer who made it.</summary>
    Task<BookingResult<BookingDetail>> GetAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct);

    /// <summary>What this customer has booked, newest first.</summary>
    Task<IReadOnlyCollection<BookingDetail>> ListForCustomerAsync(
        Guid customerUserId,
        CancellationToken ct);

    /// <summary>
    /// Everything that has happened to this booking, newest first.
    ///
    /// Read from the platform's audit trail, which the venue's desk already
    /// writes to — one booking, one account of itself. What the trail keeps and
    /// this does not is who did it and from where: that is for the platform to
    /// answer questions with, not for the person whose booking it is.
    /// </summary>
    Task<BookingResult<IReadOnlyCollection<BookingHistoryEntry>>> HistoryAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// What moving this booking onto that court, at those hours, would come to
    /// — asked before anything is committed to.
    ///
    /// It also answers whether the hours still to be played fall on the venue's
    /// today, which is what the move screen needs to know before it can offer
    /// dates: a booking today can change its hours but not its day.
    /// </summary>
    Task<BookingResult<MoveQuoteResponse>> QuoteMoveAsync(
        Guid bookingId,
        Guid customerUserId,
        Guid toBookableCourtId,
        IReadOnlyCollection<BookingSlotInput>? wanted,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves a booking onto another court, and optionally onto other hours.
    ///
    /// It happens at once: nobody is asked to approve it. A dearer court has to be
    /// paid for, anything waiting to be paid for needs a clock, and putting a
    /// settled booking back into a paying state would let that clock expire
    /// something the customer has already paid. The booking moves when the
    /// venue confirms; until then it is exactly where it was.
    ///
    /// A cheaper court asks for nothing and gives nothing back. There are no
    /// refunds — that is what a move is instead of.
    /// </summary>
    Task<BookingResult<BookingDetail>> MoveAsync(
        Guid bookingId,
        Guid customerUserId,
        MoveBookingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks to move onto hours that cost more, and offers to pay the
    /// difference.
    ///
    /// The booking does not move. It is written down as a request, the new
    /// hours are held on a clock, and the customer is sent to pay — because
    /// this is the one move where money has to change hands, and a venue
    /// should see it arrive before it gives up the better court.
    ///
    /// A move that costs the same or less does not come through here at all:
    /// that one happens at once, through <see cref="MoveAsync" />.
    /// </summary>
    Task<BookingResult<UpgradeRequestResponse>> RequestUpgradeAsync(
        Guid bookingId,
        Guid customerUserId,
        MoveBookingRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The upgrade still open on this booking, or nothing when there is none.
    ///
    /// What the upgrade screen reads on every visit to decide which step to
    /// show, so a refresh or a second tab lands where the customer actually is
    /// rather than back at the beginning.
    /// </summary>
    Task<BookingResult<UpgradeRequestResponse?>> OpenUpgradeAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the receipt for an upgrade the customer has paid.
    ///
    /// Confirms nothing. What it does do is stop the clock: from here the hours
    /// are held until the venue answers, because somebody who has paid must not
    /// lose them to a queue they are not in.
    /// </summary>
    Task<BookingResult<UpgradeRequestResponse>> AttachUpgradeReceiptAsync(
        Guid bookingId,
        Guid customerUserId,
        AttachReceiptRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hands the upgrade to the venue to check. From here it is their turn, and
    /// the booking still has not moved.
    /// </summary>
    Task<BookingResult<UpgradeRequestResponse>> SubmitUpgradeAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes the GCash receipt and hands the booking to the venue, in one go.
    ///
    /// One action, because they were two and the gap between them was a hole:
    /// attaching a receipt stops the hold's clock, so a booking that was never
    /// submitted held its court indefinitely while sitting in neither of the
    /// desk's queues. Confirms nothing — a person at the venue still has to
    /// look at it.
    ///
    /// Also takes a replacement while the venue is still looking, so a wrong
    /// picture can be corrected without ringing anybody.
    /// </summary>
    Task<BookingResult<BookingDetail>> AttachReceiptAsync(
        Guid bookingId,
        Guid customerUserId,
        AttachReceiptRequest request,
        CancellationToken ct);

    /// <summary>
    /// Lets go of the court. The row stays: a cancelled booking is part of the
    /// record, and the hours simply go back on sale.
    /// </summary>
    Task<BookingResult<bool>> CancelAsync(
        Guid bookingId,
        Guid customerUserId,
        string? reason,
        CancellationToken ct);
}
