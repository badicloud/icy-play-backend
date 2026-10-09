using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// The difference on a move to dearer hours, paid through the gateway.
///
/// On a venue paid online, the payment is the venue's agreement: when it
/// arrives in time and the hours are still free, the booking moves with nobody
/// at the desk asked — by the same rule a person's approval goes through, so
/// the two can never disagree about whether a move was sound. A free move has
/// no payment, and still goes to the desk.
/// </summary>
public sealed class UpgradePaymentHandler(
    AppDbContext db,
    DeskService desk,
    IBookingNotifier notifier,
    IOptions<BookingNotificationOptions> options,
    TimeProvider timeProvider) : IPaymentPurposeHandler
{
    private static readonly AuditActor Gateway = new(null, "PaymentGateway");

    public string Purpose => PaymentPurpose.BookingUpgrade;

    public async Task<PaymentResult<Payable>> DescribeAsync(Guid subjectId, Guid customerUserId, CancellationToken ct)
    {
        var row = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Where(upgrade => upgrade.Id == subjectId && upgrade.Booking.CustomerUserId == customerUserId)
            .Select(upgrade => new
            {
                Upgrade = upgrade,
                upgrade.Booking.CourtName,
                upgrade.Booking.FacilityName,
                upgrade.Booking.BookableCourt.Court.Facility.FacilityOwnerId,
                upgrade.Booking.BookableCourt.Court.FacilityId
            })
            .SingleOrDefaultAsync(ct);

        // Another customer's upgrade answers the same as one that does not exist.
        if (row is null || row.Upgrade.IsFree)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotFound);
        }

        var upgrade = row.Upgrade;

        if (!upgrade.IsPaidDirect)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotPaidOnline);
        }

        if (upgrade.Status != UpgradeStatus.AwaitingPayment)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotAwaitingPayment);
        }

        if (timeProvider.GetUtcNow() >= upgrade.HoldsUntil)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.HoldExpired);
        }

        // Court rental only. An upgrade buys no hours, so it carries no
        // platform fee: the whole difference is the venue's.
        return PaymentResult<Payable>.Success(new Payable(
            row.FacilityOwnerId,
            row.FacilityId,
            upgrade.BalanceDue,
            0m,
            UpgradeReference.For(upgrade.Id),
            $"Move from {row.CourtName} to {upgrade.ToCourtName} at {row.FacilityName}",
            [new CheckoutLineItem($"Upgrade to {upgrade.ToCourtName}", upgrade.BalanceDue)],
            $"{options.Value.BookingUrl.TrimEnd('/')}/{upgrade.BookingId}/upgrade"));
    }

    public async Task<Settlement> SettleAsync(OnlinePayment payment, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var upgrade = await db.BookingUpgradeRequests
            .Include(row => row.Slots)
            .Include(row => row.Booking)
            .ThenInclude(booking => booking.Slots)
            .SingleOrDefaultAsync(row => row.Id == payment.SubjectId, ct);

        if (upgrade is null)
        {
            return Settlement.NeedsAttention(AttentionReason.SubjectNotFound);
        }

        var paidAt = payment.PaidAt ?? now;

        if (!upgrade.CanBeSettledOnlineBy(paidAt))
        {
            return Settlement.NeedsAttention(
                upgrade.Status == UpgradeStatus.AwaitingPayment
                    ? AttentionReason.PaidAfterHoldLapsed
                    : AttentionReason.NotAwaitingPayment);
        }

        var (failure, _) = await desk.CarryOutUpgradeAsync(
            upgrade,
            Gateway,
            () => upgrade.ApprovePaidOnline(paidAt, now),
            now,
            ct);

        // Paid in time, but somebody else got to the hours first, or the
        // booking has moved on since. The money is real; the venue decides.
        return failure == DeskFailure.None
            ? Settlement.Done
            : Settlement.NeedsAttention(AttentionReason.UpgradeHoursTaken);
    }

    public async Task AfterSettledAsync(OnlinePayment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var upgrade = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Include(row => row.Booking)
            .SingleOrDefaultAsync(row => row.Id == payment.SubjectId, ct);

        if (upgrade is not null)
        {
            // The one letter in this flow that can say which court to walk to.
            await notifier.UpgradeApprovedAsync(upgrade, ct);
        }
    }
}
