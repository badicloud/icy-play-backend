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
    /// Carries a booking to another date, keeping its hours, its court and its
    /// price.
    ///
    /// Three times at most, never inside the last day before it starts, and
    /// only onto the same kind of day — a weekday for a weekday, a weekend for
    /// a weekend, an ordinary day for an ordinary day. Those rules exist to
    /// keep the total identical: the money is already with the venue, so a move
    /// that changed the price would need a second payment or a refund, and
    /// neither is something this platform can do.
    /// </summary>
    Task<BookingResult<BookingDetail>> MoveAsync(
        Guid bookingId,
        Guid customerUserId,
        MoveBookingRequest request,
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
