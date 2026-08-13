namespace IcyPlay.Domain.Bookings;

public enum BookingStatus
{
    PendingPayment = 1,
    PendingVerification = 2,
    Confirmed = 3,
    Rejected = 4,
    Cancelled = 5,
    Expired = 6
}
