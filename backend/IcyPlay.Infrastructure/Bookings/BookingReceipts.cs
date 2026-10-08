using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// A booking's reference, as the customer and the payment gateway both see it.
/// One rule, so the receipt, the email and the gateway's own receipt agree.
/// </summary>
internal static class BookingReference
{
    public static string For(Guid bookingId) =>
        $"BK-{bookingId.ToString("N")[..10].ToUpperInvariant()}";
}

/// <summary>
/// Reads a booking's receipt: the hours, the booking's own price, and every
/// online payment made against it with the gateway's fee broken out. Shared by
/// the receipt page and the confirmation email, so the two never disagree.
/// </summary>
internal static class BookingReceipts
{
    public static async Task<BookingReceipt?> ReadAsync(AppDbContext db, Guid bookingId, CancellationToken ct)
    {
        var row = await db.Bookings
            .AsNoTracking()
            .Include(booking => booking.Slots)
            .Where(booking => booking.Id == bookingId)
            .Select(booking => new
            {
                Booking = booking,
                Customer = db.Users
                    .Where(user => user.Id == booking.CustomerUserId)
                    .Select(user => new { user.FullName, user.Email })
                    .FirstOrDefault(),
                booking.BookableCourt.Court.Facility.ContactPhone,
                booking.BookableCourt.Court.Facility.ContactEmail
            })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var booking = row.Booking;

        // The booking's own payment and those for upgrades since, each settled.
        var upgradeIds = db.BookingUpgradeRequests
            .Where(upgrade => upgrade.BookingId == bookingId)
            .Select(upgrade => upgrade.Id);

        var payments = await db.OnlinePayments
            .AsNoTracking()
            .Where(payment => payment.Status == OnlinePaymentStatus.Paid
                && ((payment.Purpose == PaymentPurpose.Booking && payment.SubjectId == bookingId)
                    || (payment.Purpose == PaymentPurpose.BookingUpgrade && upgradeIds.Contains(payment.SubjectId))))
            .OrderBy(payment => payment.PaidAt)
            .ToListAsync(ct);

        var lines = payments
            .Select(payment => new ReceiptPayment(
                payment.Purpose == PaymentPurpose.BookingUpgrade ? "Upgrade" : "Booking",
                payment.PaymentMethod,
                payment.AmountCharged ?? payment.AmountDue,
                payment.ProcessingFee ?? 0m,
                payment.PaidAt,
                payment.ProviderPaymentId))
            .ToArray();

        var processingFees = lines.Sum(line => line.ProcessingFee);

        // Paid online, what was handed over is what the gateway charged. Paid
        // by receipt, it is what the venue confirmed, which has no fee on it.
        var amountPaid = lines.Length > 0
            ? lines.Sum(line => line.AmountCharged)
            : booking.PaidTotal;

        return new BookingReceipt(
            BookingReference.For(booking.Id),
            booking.Id,
            row.Customer?.FullName ?? string.Empty,
            row.Customer?.Email ?? string.Empty,
            booking.CourtName,
            booking.FacilityName,
            booking.SportName,
            row.ContactPhone,
            row.ContactEmail,
            [
                .. booking.Slots
                    .OrderBy(slot => slot.Date)
                    .ThenBy(slot => slot.StartsAt)
                    .Select(slot => new BookedSlot(
                        slot.Date,
                        slot.StartsAt,
                        slot.EndsAt,
                        slot.RateKind.ToString(),
                        slot.Amount,
                        slot.PlatformFee))
            ],
            booking.RentalTotal,
            booking.PlatformFeeTotal,
            booking.Total,
            booking.PaymentChannel,
            lines,
            processingFees,
            amountPaid,
            booking.ConfirmedAt);
    }
}

public sealed class BookingReceiptService(AppDbContext db) : IBookingReceiptService
{
    public async Task<BookingResult<BookingReceipt>> GetAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var status = await db.Bookings
            .AsNoTracking()
            .Where(booking => booking.Id == bookingId && booking.CustomerUserId == customerUserId)
            .Select(booking => (BookingStatus?)booking.Status)
            .SingleOrDefaultAsync(ct);

        // Another customer's booking answers the same as one that does not exist.
        if (status is null)
        {
            return BookingResult<BookingReceipt>.Fail(BookingFailure.CourtNotFound);
        }

        if (status != BookingStatus.Confirmed)
        {
            return BookingResult<BookingReceipt>.Fail(BookingFailure.NotConfirmed);
        }

        var receipt = await BookingReceipts.ReadAsync(db, bookingId, ct);

        return receipt is null
            ? BookingResult<BookingReceipt>.Fail(BookingFailure.CourtNotFound)
            : BookingResult<BookingReceipt>.Success(receipt);
    }
}
