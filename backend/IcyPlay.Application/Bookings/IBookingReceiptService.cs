namespace IcyPlay.Application.Bookings;

public interface IBookingReceiptService
{
    /// <summary>
    /// The receipt for one of this customer's own bookings, once it is
    /// confirmed. Before that there is nothing paid to account for.
    /// </summary>
    Task<BookingResult<BookingReceipt>> GetAsync(Guid bookingId, Guid customerUserId, CancellationToken ct);
}
