using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// One player's spot in one <see cref="OpenPlaySession"/>.
///
/// Paid the same way as a court booking: the player sends a GCash receipt and
/// the desk confirms it. That is why it reuses <see cref="BookingStatus"/>.
/// The price is a snapshot, like a booking's, so editing the open play or the
/// owner's contract later cannot change what this player agreed to pay.
/// </summary>
public sealed class OpenPlayRegistration : Entity
{
    public const string SessionCancelledReason = "The venue cancelled this session.";

    private OpenPlayRegistration()
    {
    }

    public OpenPlayRegistration(
        Guid sessionId,
        Guid customerUserId,
        OpenPlayPrice price,
        int holdMinutes,
        DateTimeOffset createdAt)
    {
        SessionId = sessionId;
        CustomerUserId = customerUserId;
        RegistrationFee = price.RegistrationFee;
        Discount = price.Discount;
        PlatformFee = price.PlatformFee;
        HoldsUntil = createdAt.AddMinutes(PaymentHold.Clamp(holdMinutes));
        Status = BookingStatus.PendingPayment;
        CreatedAt = createdAt;
    }

    public Guid SessionId
    {
        get; private set;
    }
    public OpenPlaySession Session { get; private set; } = null!;

    public Guid CustomerUserId
    {
        get; private set;
    }

    public BookingStatus Status { get; private set; } = BookingStatus.PendingPayment;

    /// <summary>The venue's fee as it stood at registration.</summary>
    public decimal RegistrationFee
    {
        get; private set;
    }

    /// <summary>The early-bird discount, or zero if the player registered too late for it.</summary>
    public decimal Discount
    {
        get; private set;
    }

    /// <summary>The platform's top-up, from the owner's contract at registration.</summary>
    public decimal PlatformFee
    {
        get; private set;
    }

    public decimal Total => RegistrationFee - Discount + PlatformFee;

    public DateTimeOffset HoldsUntil
    {
        get; private set;
    }

    public string? ReceiptUrl
    {
        get; private set;
    }

    public DateTimeOffset? ReceiptUploadedAt
    {
        get; private set;
    }

    public DateTimeOffset? SubmittedForVerificationAt
    {
        get; private set;
    }

    public DateTimeOffset? ConfirmedAt
    {
        get; private set;
    }

    public DateTimeOffset? CancelledAt
    {
        get; private set;
    }

    public string? CancellationReason
    {
        get; private set;
    }

    /// <summary>
    /// Whether this registration counts against the session's spots. Works the
    /// same as <see cref="Booking.HoldsTheCourtAt"/>: a receipt stops the clock.
    /// </summary>
    public bool HoldsSpotAt(DateTimeOffset moment) =>
        BookingStatuses.IsLive(Status) && !HasLapsedAt(moment);

    public bool HasLapsedAt(DateTimeOffset moment) =>
        Status == BookingStatus.PendingPayment && ReceiptUrl is null && moment >= HoldsUntil;

    public void AttachReceipt(string receiptUrl, DateTimeOffset now)
    {
        ReceiptUrl = receiptUrl;
        ReceiptUploadedAt = now;
        UpdatedAt = now;
    }

    public void SubmitForVerification(DateTimeOffset now)
    {
        Status = BookingStatus.PendingVerification;
        SubmittedForVerificationAt = now;
        UpdatedAt = now;
    }

    public void Confirm(DateTimeOffset now)
    {
        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The desk looked at the receipt and the money is not there.</summary>
    public void Reject(string reason, DateTimeOffset now)
    {
        Status = BookingStatus.Rejected;
        CancelledAt = now;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        if (!BookingStatuses.IsLive(Status))
        {
            return;
        }

        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = now;
    }
}
