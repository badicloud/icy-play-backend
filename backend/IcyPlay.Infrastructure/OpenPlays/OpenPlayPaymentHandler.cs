using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// A player's place at an open play session, paid through the gateway: what
/// it costs, and registering them when the gateway says it is paid.
/// </summary>
public sealed class OpenPlayPaymentHandler(
    AppDbContext db,
    IOpenPlayNotifier notifier,
    IAuditLogger audit,
    IOptions<BookingNotificationOptions> options,
    TimeProvider timeProvider) : IPaymentPurposeHandler
{
    private static readonly AuditActor Gateway = new(null, "PaymentGateway");

    public string Purpose => PaymentPurpose.OpenPlayRegistration;

    public async Task<PaymentResult<Payable>> DescribeAsync(Guid subjectId, Guid customerUserId, CancellationToken ct)
    {
        var row = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.Id == subjectId && registration.CustomerUserId == customerUserId)
            .Select(registration => new
            {
                Registration = registration,
                registration.Session.Date,
                registration.Session.OpenPlay.Title,
                FacilityName = registration.Session.OpenPlay.Facility.Name,
                registration.Session.OpenPlay.Facility.FacilityOwnerId,
                registration.Session.OpenPlay.FacilityId
            })
            .SingleOrDefaultAsync(ct);

        // Another player's registration answers the same as one that does not exist.
        if (row is null)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotFound);
        }

        var registration = row.Registration;

        if (!registration.IsPaidDirect)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotPaidOnline);
        }

        if (registration.Status != BookingStatus.PendingPayment)
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.NotAwaitingPayment);
        }

        if (registration.HasLapsedAt(timeProvider.GetUtcNow()))
        {
            return PaymentResult<Payable>.Fail(PaymentFailure.HoldExpired);
        }

        // What the venue is owed is the fee less any early-bird discount; the
        // platform's fee sits beside it, the same split a booking has.
        var venueAmount = registration.RegistrationFee - registration.Discount;
        var lines = new List<CheckoutLineItem>
        {
            new($"{row.Title}, {row.Date:d MMM yyyy}", venueAmount)
        };

        if (registration.PlatformFee > 0m)
        {
            lines.Add(new CheckoutLineItem("IcyPlay platform fee", registration.PlatformFee));
        }

        return PaymentResult<Payable>.Success(new Payable(
            row.FacilityOwnerId,
            row.FacilityId,
            venueAmount,
            registration.PlatformFee,
            $"OP-{registration.Id.ToString("N")[..10].ToUpperInvariant()}",
            $"{row.Title} at {row.FacilityName}",
            lines,
            $"{options.Value.OpenPlayRegistrationUrl.TrimEnd('/')}/{registration.Id}"));
    }

    public async Task<Settlement> SettleAsync(OnlinePayment payment, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);

        var registration = await db.OpenPlayRegistrations
            .SingleOrDefaultAsync(candidate => candidate.Id == payment.SubjectId, ct);

        if (registration is null)
        {
            return Settlement.NeedsAttention(AttentionReason.SubjectNotFound);
        }

        var paidAt = payment.PaidAt ?? now;

        if (registration.CanBeConfirmedOnlineBy(paidAt))
        {
            registration.ConfirmPaidOnline(paidAt, now);
            Record(AuditAction.OpenPlayPaidOnline, registration, payment, "Paid online. Registered.");

            return Settlement.Done;
        }

        // Paid, but the hold had run out — the spot may be somebody else's —
        // or the registration was settled some other way in the meantime.
        var reason = registration.Status == BookingStatus.PendingPayment && registration.IsPaidDirect
            ? AttentionReason.PaidAfterHoldLapsed
            : AttentionReason.NotAwaitingPayment;

        Record(AuditAction.OpenPlayOnlinePaymentNeedsAttention, registration, payment, reason);

        return Settlement.NeedsAttention(reason);
    }

    public Task AfterSettledAsync(OnlinePayment payment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payment);

        // The player's confirmation, with their check-in pass. The desk is not
        // asked to do anything; the session's list is where it will see them.
        return notifier.ConfirmedAsync(payment.SubjectId, ct);
    }

    private void Record(string action, OpenPlayRegistration registration, OnlinePayment payment, string description) =>
        audit.RecordEvent(
            Gateway,
            action,
            AuditEntityType.OpenPlayRegistration,
            registration.Id,
            new Dictionary<string, string?>
            {
                ["payment_method"] = payment.PaymentMethod,
                ["payment_reference"] = payment.ProviderPaymentId,
                ["amount_charged"] = payment.AmountCharged?.ToString("0.00"),
                ["processing_fee"] = payment.ProcessingFee?.ToString("0.00")
            },
            description);
}
