using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// A court booking's first payment, made through the gateway: what it owes,
/// and confirming it when the gateway says it is paid.
/// </summary>
public sealed class BookingPaymentHandler(
    AppDbContext db,
    IBookingNotifier notifier,
    IAuditLogger audit,
    IOptions<BookingNotificationOptions> options,
    TimeProvider timeProvider) : IPaymentPurposeHandler
{
    /// <summary>Who the trail says did it: nobody signed in, the gateway's word.</summary>
    private static readonly AuditActor Gateway = new(null, "PaymentGateway");

    public string Purpose => PaymentPurpose.Booking;

    public async Task<PaymentResult<Payable>> DescribeAsync(Guid subjectId, Guid customerUserId, CancellationToken ct)
    {
        var row = await db.Bookings
            .AsNoTracking()
            .Include(booking => booking.Slots)
            .Where(booking => booking.Id == subjectId && booking.CustomerUserId == customerUserId)
            .Select(booking => new
            {
                Booking = booking,
                booking.BookableCourt.Court.Facility.FacilityOwnerId,
                booking.BookableCourt.Court.FacilityId
            })
            .SingleOrDefaultAsync(ct);

        // Another customer's booking answers the same as one that does not exist.
        if (row is null)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotFound);
        }

        var booking = row.Booking;

        if (!booking.IsPaidDirect)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotPaidOnline);
        }

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotAwaitingPayment);
        }

        if (booking.HasLapsedAt(timeProvider.GetUtcNow()))
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.HoldExpired);
        }

        var hours = booking.BookedHours == 1 ? "1 hour" : $"{booking.BookedHours} hours";
        var lines = new List<CheckoutLineItem>
        {
            new($"{booking.CourtName}, {hours}", booking.RentalTotal)
        };

        // A venue onboarded with no platform fee has nothing to show for it,
        // and a line of zero is a line the gateway may refuse.
        if (booking.PlatformFeeTotal > 0m)
        {
            lines.Add(new CheckoutLineItem("IcyPlay platform fee", booking.PlatformFeeTotal));
        }

        return PaymentResult<Payable>.Success(new Payable(
            row.FacilityOwnerId,
            row.FacilityId,
            booking.RentalTotal,
            booking.PlatformFeeTotal,
            Reference(booking.Id),
            $"{booking.CourtName} at {booking.FacilityName}",
            lines,
            BookingUrl(booking.Id)));
    }

    public async Task<Settlement> SettleAsync(OnlinePayment payment, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var booking = await db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(candidate => candidate.Id == payment.SubjectId, ct);

        if (booking is null)
        {
            return Settlement.NeedsAttention(AttentionReason.SubjectNotFound);
        }

        var paidAt = payment.PaidAt ?? now;

        if (booking.CanBeConfirmedOnlineBy(paidAt))
        {
            booking.ConfirmPaidOnline(paidAt, now);
            Record(AuditAction.BookingPaidOnline, booking, payment, "Paid online and confirmed.");

            return Settlement.Done;
        }

        // Paid, but not in a way that can confirm anything by itself. The hold
        // ran out first — the hours may already be somebody else's — or the
        // booking was paid, cancelled or refused in the meantime. The money is
        // real either way, and the venue decides what becomes of it.
        var reason = booking.Status == BookingStatus.PendingPayment && booking.IsPaidDirect
            ? AttentionReason.PaidAfterHoldLapsed
            : AttentionReason.NotAwaitingPayment;

        // The description is what the customer's history shows, so it is said
        // to them; the reason code is for the desk.
        Record(
            AuditAction.BookingOnlinePaymentNeedsAttention,
            booking,
            payment,
            reason == AttentionReason.PaidAfterHoldLapsed
                ? "Your online payment arrived after the hold ran out. The venue will get in touch about it."
                : "An online payment arrived for this booking, and the venue is looking at it.",
            reason);

        return Settlement.NeedsAttention(reason);
    }

    public async Task AfterSettledAsync(OnlinePayment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var booking = await db.Bookings
            .AsNoTracking()
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(candidate => candidate.Id == payment.SubjectId, ct);

        if (booking is null)
        {
            return;
        }

        // The customer's confirmation and the venue's notice. Neither is asked
        // to do anything; both need to know the court is spoken for.
        await notifier.BookingConfirmedAsync(booking, ct);
        await notifier.BookingPaidOnlineAsync(booking, payment, ct);
    }

    /// <summary>
    /// Short enough to read out over the phone, and enough of the id to find
    /// the booking from a line on the gateway's export.
    /// </summary>
    private static string Reference(Guid bookingId) =>
        BookingReference.For(bookingId);

    private string BookingUrl(Guid bookingId) =>
        $"{options.Value.BookingUrl.TrimEnd('/')}/{bookingId}";

    private void Record(
        string action,
        Booking booking,
        OnlinePayment payment,
        string description,
        string? attentionReason = null) =>
        audit.RecordEvent(
            Gateway,
            action,
            AuditEntityType.Booking,
            booking.Id,
            new Dictionary<string, string?>
            {
                ["description"] = description,
                ["court"] = booking.CourtName,
                ["total"] = booking.Total.ToString("0.00"),
                ["payment_method"] = payment.PaymentMethod,
                ["payment_reference"] = payment.ProviderPaymentId,
                ["amount_charged"] = payment.AmountCharged?.ToString("0.00"),
                ["processing_fee"] = payment.ProcessingFee?.ToString("0.00"),
                ["attention_reason"] = attentionReason
            });
}
