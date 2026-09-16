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
    /// What moving this booking to another court would cost, before anybody
    /// commits to it. "Move this booking" and "move it and pay another twelve
    /// hundred pesos" are different questions, and only one can be answered
    /// with a tap.
    /// </summary>
    Task<BookingResult<MoveQuoteResponse>> QuoteMoveAsync(
        Guid bookingId,
        Guid customerUserId,
        Guid toBookableCourtId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks to move a booking onto another court, keeping its hours.
    ///
    /// Raises a request rather than moving anything. A dearer court has to be
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

    /// <summary>The GCash receipt for the difference. The hold's clock stops here.</summary>
    Task<BookingResult<BookingDetail>> AttachMoveReceiptAsync(
        Guid bookingId,
        Guid customerUserId,
        string receiptUrl,
        CancellationToken cancellationToken);

    /// <summary>The customer thought better of it. The held court goes back.</summary>
    Task<BookingResult<BookingDetail>> WithdrawMoveAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The venue moves a booking itself, because the court it is on has a
    /// problem. Immediate: the players are standing on it.
    ///
    /// A dearer court needs a waiver and a reason for it. An attendant cannot
    /// take money from somebody who is not in the conversation — either the
    /// customer is asked to upgrade, or the venue absorbs the difference and
    /// says who decided that.
    /// </summary>
    Task<BookingResult<BookingDetail>> MoveByVenueAsync(
        Guid bookingId,
        Guid attendantUserId,
        Guid toBookableCourtId,
        string reason,
        string? waiverReason,
        CancellationToken cancellationToken);

    /// <summary>The venue has looked at the payment, and the booking moves.</summary>
    Task<BookingResult<BookingDetail>> ConfirmMoveAsync(
        Guid bookingId,
        Guid attendantUserId,
        CancellationToken cancellationToken);

    /// <summary>The venue lets the customer off the difference, and says why.</summary>
    Task<BookingResult<BookingDetail>> WaiveMoveAsync(
        Guid bookingId,
        Guid attendantUserId,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>The venue says no. The booking stays exactly where it was.</summary>
    Task<BookingResult<BookingDetail>> DeclineMoveAsync(
        Guid bookingId,
        Guid attendantUserId,
        string? reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the GCash receipt the customer uploaded. Confirms nothing: a
    /// person at the venue still has to look at it.
    /// </summary>
    Task<BookingResult<BookingDetail>> AttachReceiptAsync(
        Guid bookingId,
        Guid customerUserId,
        AttachReceiptRequest request,
        CancellationToken ct);

    /// <summary>
    /// Hands the booking to the venue to check, and tells both sides by email.
    /// From here the hold stops mattering — somebody who has paid must not lose
    /// their court because the venue was asleep.
    /// </summary>
    Task<BookingResult<BookingDetail>> SubmitForVerificationAsync(
        Guid bookingId,
        Guid customerUserId,
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
