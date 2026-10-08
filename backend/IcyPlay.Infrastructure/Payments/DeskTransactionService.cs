using IcyPlay.Application.Common;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Payments;

/// <summary>
/// The online payments at a desk's venues. The record a venue paid online
/// keeps in place of the receipt queue: nothing to check, but everything to
/// know about — and the few that could not settle themselves, kept at the top.
/// </summary>
public sealed class DeskTransactionService(AppDbContext db, TimeProvider timeProvider) : IDeskTransactionService
{
    private const int LargestPage = 50;

    public async Task<PagedResult<DeskTransaction>> ListAsync(
        Guid userId,
        Guid? facilityId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, LargestPage);

        var seenAt = await SeenAtAsync(userId, ct);
        var settled = Settled(userId);

        if (facilityId is Guid only)
        {
            settled = settled.Where(payment => payment.FacilityId == only);
        }

        var total = await settled.CountAsync(ct);

        var rows = await settled
            // A payment waiting on a person is the one thing here somebody
            // has to act on, so it does not sink under newer ones.
            .OrderBy(payment => payment.Status == OnlinePaymentStatus.NeedsAttention ? 0 : 1)
            .ThenByDescending(payment => payment.PaidAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(payment => new
            {
                Payment = payment,
                FacilityName = db.Facilities
                    .Where(facility => facility.Id == payment.FacilityId)
                    .Select(facility => facility.Name)
                    .FirstOrDefault(),
                Customer = db.Users
                    .Where(user => user.Id == payment.CustomerUserId)
                    .Select(user => new { user.FullName, user.Email })
                    .FirstOrDefault(),
                UpgradeBookingId = db.BookingUpgradeRequests
                    .Where(upgrade => upgrade.Id == payment.SubjectId)
                    .Select(upgrade => (Guid?)upgrade.BookingId)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return new PagedResult<DeskTransaction>(
            [
                .. rows.Select(row => new DeskTransaction(
                    row.Payment.Id,
                    row.Payment.FacilityId,
                    row.FacilityName ?? string.Empty,
                    row.Payment.Purpose,
                    row.Payment.Description,
                    row.Customer?.FullName ?? string.Empty,
                    row.Customer?.Email ?? string.Empty,
                    row.Payment.PaymentMethod,
                    row.Payment.AmountCharged,
                    row.Payment.ProcessingFee,
                    row.Payment.NetAmount,
                    row.Payment.VenueAmount,
                    row.Payment.PlatformFee,
                    row.Payment.ProviderPaymentId,
                    row.Payment.Status,
                    row.Payment.AttentionReason,
                    row.Payment.PaidAt,
                    row.Payment.Purpose switch
                    {
                        PaymentPurpose.Booking => row.Payment.SubjectId,
                        PaymentPurpose.BookingUpgrade => row.UpgradeBookingId,
                        _ => null
                    },
                    seenAt is null || row.Payment.PaidAt > seenAt))
            ],
            page,
            pageSize,
            total);
    }

    public async Task<DeskTransactionSummary> SummaryAsync(Guid userId, CancellationToken ct)
    {
        var seenAt = await SeenAtAsync(userId, ct);
        var settled = Settled(userId);

        var unseen = await (seenAt is DateTimeOffset since
                ? settled.Where(payment => payment.PaidAt > since)
                : settled)
            .CountAsync(ct);

        var needsAttention = await settled
            .CountAsync(payment => payment.Status == OnlinePaymentStatus.NeedsAttention, ct);

        return new DeskTransactionSummary(unseen, needsAttention);
    }

    public async Task MarkSeenAsync(Guid userId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var marker = await db.DeskReadMarkers
            .SingleOrDefaultAsync(row => row.UserId == userId && row.Feed == DeskFeed.OnlineTransactions, ct);

        if (marker is null)
        {
            db.DeskReadMarkers.Add(new DeskReadMarker(userId, DeskFeed.OnlineTransactions, now));
        }
        else
        {
            marker.SeenUpTo(now);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Paid payments at venues this person works and may see the money of:
    /// the owner always, an attendant only once the owner has shared it.
    /// A checkout still open is not a transaction yet.
    /// </summary>
    private IQueryable<OnlinePayment> Settled(Guid userId)
    {
        var venueIds = db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant =>
                    attendant.UserId == userId && attendant.IsActive && attendant.CanSeeMoney))
            .Select(facility => facility.Id);

        return db.OnlinePayments
            .AsNoTracking()
            .Where(payment => venueIds.Contains(payment.FacilityId)
                && payment.Status != OnlinePaymentStatus.Pending);
    }

    private async Task<DateTimeOffset?> SeenAtAsync(Guid userId, CancellationToken ct) =>
        await db.DeskReadMarkers
            .AsNoTracking()
            .Where(row => row.UserId == userId && row.Feed == DeskFeed.OnlineTransactions)
            .Select(row => (DateTimeOffset?)row.SeenAt)
            .FirstOrDefaultAsync(ct);
}
