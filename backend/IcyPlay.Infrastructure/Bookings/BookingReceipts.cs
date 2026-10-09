using System.Globalization;
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

/// <summary>An upgrade's reference: its own receipt, and its line on the gateway.</summary>
internal static class UpgradeReference
{
    public static string For(Guid upgradeId) =>
        $"UP-{upgradeId.ToString("N")[..10].ToUpperInvariant()}";
}

/// <summary>
/// Reads a booking's receipt: what the booking itself was bought for and
/// paid. An upgrade is a payment of its own with a receipt of its own; this
/// only names it, so the two are never added together on one page.
///
/// Shared by the receipt page and the confirmation email, so the two never
/// disagree.
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

        var payment = await db.OnlinePayments
            .AsNoTracking()
            .Where(candidate => candidate.Status == OnlinePaymentStatus.Paid
                && candidate.Purpose == PaymentPurpose.Booking
                && candidate.SubjectId == bookingId)
            .OrderBy(candidate => candidate.PaidAt)
            .FirstOrDefaultAsync(ct);

        var moves = await db.BookingMoves
            .AsNoTracking()
            .Where(move => move.BookingId == bookingId)
            .OrderBy(move => move.MovedAt)
            .ToListAsync(ct);

        var upgrades = await UpgradesAsync(db, bookingId, moves, ct);

        var lines = payment is null
            ? Array.Empty<ReceiptPayment>()
            :
            [
                new ReceiptPayment(
                    "Booking",
                    payment.PaymentMethod,
                    payment.AmountCharged ?? payment.AmountDue,
                    payment.ProcessingFee ?? 0m,
                    payment.PaidAt,
                    payment.ProviderPaymentId)
            ];

        // Never moved: the hours on the booking are the ones that were paid
        // for, and can be listed with their prices.
        if (moves.Count == 0)
        {
            return Receipt(row.Customer?.FullName, row.Customer?.Email, row.ContactPhone, row.ContactEmail, booking,
                booking.CourtName,
                SlotsOf(booking),
                booking.RentalTotal,
                booking.Total,
                lines,
                payment?.AmountCharged ?? booking.PaidTotal,
                originalSummary: null,
                upgrades);
        }

        // Moved since: the hours now are on another court, at another price,
        // and listing them under this payment would say it bought them. What
        // it bought is said in a sentence instead, at what was paid. Online,
        // that is the payment itself; by receipt, it is what was paid less
        // any upgrades approved since.
        var originalTotal = payment?.AmountDue
            ?? booking.PaidTotal - upgrades.Sum(upgrade => upgrade.Difference);
        var originalCourt = moves[0].FromCourtName ?? booking.CourtName;
        var hours = booking.BookedHours == 1 ? "1 hour" : $"{booking.BookedHours} hours";

        return Receipt(row.Customer?.FullName, row.Customer?.Email, row.ContactPhone, row.ContactEmail, booking,
            originalCourt,
            [],
            originalTotal - booking.PlatformFeeTotal,
            originalTotal,
            lines,
            payment?.AmountCharged ?? originalTotal,
            $"{hours} on {originalCourt}, as first booked",
            upgrades);
    }

    private static BookingReceipt Receipt(
        string? customerName,
        string? customerEmail,
        string? contactPhone,
        string? contactEmail,
        Booking booking,
        string courtName,
        IReadOnlyCollection<BookedSlot> slots,
        decimal rental,
        decimal total,
        IReadOnlyCollection<ReceiptPayment> lines,
        decimal amountPaid,
        string? originalSummary,
        IReadOnlyCollection<PaidUpgrade> upgrades) =>
        new(
            BookingReference.For(booking.Id),
            booking.Id,
            customerName ?? string.Empty,
            customerEmail ?? string.Empty,
            courtName,
            booking.FacilityName,
            booking.SportName,
            contactPhone,
            contactEmail,
            slots,
            rental,
            booking.PlatformFeeTotal,
            total,
            booking.PaymentChannel,
            lines,
            lines.Sum(line => line.ProcessingFee),
            amountPaid,
            booking.ConfirmedAt,
            originalSummary,
            [.. upgrades.Select(upgrade => upgrade.Summary)]);

    private static BookedSlot[] SlotsOf(Booking booking) =>
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
    ];

    /// <summary>The upgrades approved on a booking, each with what was paid for it.</summary>
    private static async Task<IReadOnlyCollection<PaidUpgrade>> UpgradesAsync(
        AppDbContext db,
        Guid bookingId,
        IReadOnlyList<BookingMoveRecord> moves,
        CancellationToken ct)
    {
        var approved = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Where(upgrade => upgrade.BookingId == bookingId
                && upgrade.Status == UpgradeStatus.Approved
                && upgrade.BalanceDue > 0m)
            .OrderBy(upgrade => upgrade.SettledAt)
            .ToListAsync(ct);

        if (approved.Count == 0)
        {
            return [];
        }

        var ids = approved.Select(upgrade => upgrade.Id).ToArray();
        var payments = await db.OnlinePayments
            .AsNoTracking()
            .Where(payment => payment.Purpose == PaymentPurpose.BookingUpgrade
                && payment.Status == OnlinePaymentStatus.Paid
                && ids.Contains(payment.SubjectId))
            .ToListAsync(ct);

        return
        [
            .. approved.Select(upgrade =>
            {
                var paid = payments.FirstOrDefault(payment => payment.SubjectId == upgrade.Id);

                return new PaidUpgrade(
                    upgrade.BalanceDue,
                    new UpgradeReceiptSummary(
                        UpgradeReference.For(upgrade.Id),
                        FromCourtOf(upgrade, moves),
                        upgrade.ToCourtName,
                        paid?.AmountCharged ?? upgrade.BalanceDue,
                        paid?.PaidAt ?? upgrade.SettledAt));
            })
        ];
    }

    /// <summary>
    /// The court an upgrade moved the booking from: the upgrade's own move,
    /// which is the one onto its court closest to when it was approved.
    /// </summary>
    internal static string? FromCourtOf(BookingUpgradeRequest upgrade, IReadOnlyList<BookingMoveRecord> moves) =>
        moves
            .Where(move => move.Kind == MoveKind.Upgrade && move.ToCourtName == upgrade.ToCourtName)
            .OrderBy(move => upgrade.SettledAt is DateTimeOffset settled
                ? Math.Abs((move.MovedAt - settled).Ticks)
                : 0)
            .Select(move => move.FromCourtName)
            .FirstOrDefault();

    private sealed record PaidUpgrade(decimal Difference, UpgradeReceiptSummary Summary);
}

/// <summary>
/// The receipt for one upgrade on its own: the move, the difference paid for
/// it, and the gateway's fee — never the booking's first payment beside it.
/// </summary>
internal static class UpgradeReceipts
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static async Task<ReceiptDocument?> ReadAsync(AppDbContext db, Guid upgradeId, CancellationToken ct)
    {
        var upgrade = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Include(candidate => candidate.Slots)
            .Include(candidate => candidate.Booking)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == upgradeId
                    && candidate.Status == UpgradeStatus.Approved
                    && candidate.BalanceDue > 0m,
                ct);

        if (upgrade is null)
        {
            return null;
        }

        var booking = upgrade.Booking;

        var who = await db.Bookings
            .AsNoTracking()
            .Where(candidate => candidate.Id == booking.Id)
            .Select(candidate => new
            {
                Customer = db.Users
                    .Where(user => user.Id == candidate.CustomerUserId)
                    .Select(user => new { user.FullName, user.Email })
                    .FirstOrDefault(),
                candidate.BookableCourt.Court.Facility.ContactPhone,
                candidate.BookableCourt.Court.Facility.ContactEmail
            })
            .SingleAsync(ct);

        var moves = await db.BookingMoves
            .AsNoTracking()
            .Where(move => move.BookingId == booking.Id)
            .ToListAsync(ct);

        var fromCourt = BookingReceipts.FromCourtOf(upgrade, moves) ?? "your court";

        var payment = await db.OnlinePayments
            .AsNoTracking()
            .Where(candidate => candidate.Purpose == PaymentPurpose.BookingUpgrade
                && candidate.SubjectId == upgradeId
                && candidate.Status == OnlinePaymentStatus.Paid)
            .FirstOrDefaultAsync(ct);

        var fees = payment?.ProcessingFee is decimal fee && fee > 0m
            ? new[] { new ReceiptLine("Payment processing fee", fee, $"{ReceiptPdf.MethodName(payment.PaymentMethod)}, via PayMongo · VAT incl.") }
            : [];

        var notes = payment is not null
            ? new[]
            {
                $"Paid {(payment.PaidAt is DateTimeOffset at ? ReceiptPdf.Moment(at) : string.Empty)} with " +
                $"{ReceiptPdf.MethodName(payment.PaymentMethod)}" +
                (payment.ProviderPaymentId is null ? string.Empty : $" · PayMongo ref {payment.ProviderPaymentId}"),
                $"For booking {BookingReference.For(booking.Id)}. No platform fee: an upgrade moves hours, it does not add them."
            }
            :
            [
                $"Paid by GCash to {booking.FacilityName}, and checked by the venue.",
                $"For booking {BookingReference.For(booking.Id)}. No platform fee: an upgrade moves hours, it does not add them."
            ];

        return new ReceiptDocument(
            UpgradeReference.For(upgrade.Id),
            upgrade.SettledAt,
            who.Customer?.FullName ?? string.Empty,
            who.Customer?.Email ?? string.Empty,
            booking.FacilityName,
            ReceiptPdf.Contact(who.ContactPhone, who.ContactEmail),
            $"Upgrade to {upgrade.ToCourtName}",
            $"From {fromCourt} · {booking.SportName}",
            [
                .. upgrade.Slots
                    .OrderBy(slot => slot.Date)
                    .ThenBy(slot => slot.StartsAt)
                    .Select(slot => new ReceiptLine(
                        $"{slot.Date.ToString("ddd, d MMM yyyy", Culture)} · " +
                        $"{slot.StartsAt.ToString("h:mm tt", Culture)} – {slot.EndsAt.ToString("h:mm tt", Culture)}",
                        slot.Amount))
            ],
            [
                new ReceiptLine($"Court rental on {upgrade.ToCourtName}", upgrade.RentalNew),
                new ReceiptLine($"Less what those hours cost on {fromCourt}", -upgrade.RentalNow)
            ],
            "Upgrade difference",
            upgrade.BalanceDue,
            fees,
            payment?.AmountCharged ?? upgrade.BalanceDue,
            notes);
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
