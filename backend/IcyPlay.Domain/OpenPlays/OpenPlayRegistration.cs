using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Common;
using IcyPlay.Domain.Payments;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// One player's spot in one <see cref="OpenPlaySession"/>.
///
/// Paid the same way as a court booking: the player sends a GCash receipt and
/// the desk confirms it. That is why it reuses <see cref="BookingStatus"/>.
/// The price is a snapshot, like a booking's, so editing the open play or the
/// owner's contract later cannot change what this player agreed to pay.
///
/// Only a confirmed registration is a registered player. A hold, and a receipt
/// waiting on the desk, reserve the spot so it is not sold twice while the
/// player pays, but nobody is "in" until a person at the venue has looked at
/// the money.
/// </summary>
public sealed class OpenPlayRegistration : Entity
{
    public const string SessionCancelledReason = "The venue cancelled this session.";

    private OpenPlayRegistration()
    {
    }

    /// <param name="agreedToPolicyAt">
    /// When the player ticked the open play policy: no refund through IcyPlay,
    /// because the money goes straight to the venue. Kept, because it is what
    /// the player agreed to and a dispute will ask.
    /// </param>
    public OpenPlayRegistration(
        Guid sessionId,
        Guid customerUserId,
        OpenPlayPrice price,
        int holdMinutes,
        DateTimeOffset agreedToPolicyAt,
        DateTimeOffset createdAt,
        string paymentChannel = PaymentMode.Manual)
    {
        if (!PaymentMode.IsSupported(paymentChannel))
        {
            throw new ArgumentException($"'{paymentChannel}' is not a payment mode.", nameof(paymentChannel));
        }

        SessionId = sessionId;
        PaymentChannel = paymentChannel;
        CustomerUserId = customerUserId;
        RegistrationFee = price.RegistrationFee;
        Discount = price.Discount;
        PlatformFee = price.PlatformFee;
        HoldsUntil = createdAt.AddMinutes(PaymentHold.Clamp(holdMinutes));
        AgreedToPolicyAt = agreedToPolicyAt;
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

    public DateTimeOffset AgreedToPolicyAt
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

    /// <summary>Who at the venue, or on the platform, looked at the payment and said yes.</summary>
    public Guid? ConfirmedByUserId
    {
        get; private set;
    }

    public DateTimeOffset? CancelledAt
    {
        get; private set;
    }

    /// <summary>
    /// The sentence the player reads on a refusal or a cancel: on a refusal,
    /// the reason the desk picked and its note, as <see cref="RejectReason.Sentence"/>.
    /// </summary>
    public string? CancellationReason
    {
        get; private set;
    }

    /// <summary>On a refusal, the <see cref="RejectReason"/> the desk picked.</summary>
    public string? RejectionReason
    {
        get; private set;
    }

    public string? RejectionNote
    {
        get; private set;
    }

    public Guid? RejectedByUserId
    {
        get; private set;
    }

    /// <summary>
    /// The token in this registration's check-in QR, as <see cref="CheckInPass"/>.
    /// Made when the payment is confirmed: nobody gets a QR for a spot that is
    /// not paid for. Null before that.
    /// </summary>
    public string? CheckInToken
    {
        get; private set;
    }

    /// <summary>When the desk checked the player in at the session. Null until they arrive.</summary>
    public DateTimeOffset? CheckedInAt
    {
        get; private set;
    }

    public Guid? CheckedInByUserId
    {
        get; private set;
    }

    /// <summary>A registered player: the venue has confirmed the payment.</summary>
    public bool IsRegistered => Status == BookingStatus.Confirmed;

    public bool IsCheckedIn => CheckedInAt is not null;

    /// <summary>
    /// Where the check-in QR stands, or null when there is none to show: not
    /// confirmed, or confirmed and then cancelled with the session.
    /// </summary>
    /// <param name="sessionHasEnded">On the venue's clock; the caller's to work out.</param>
    public string? PassState(bool sessionHasEnded)
    {
        if (!IsRegistered || CheckInToken is null)
        {
            return null;
        }

        if (IsCheckedIn)
        {
            return CheckInPassState.Used;
        }

        return sessionHasEnded ? CheckInPassState.Expired : CheckInPassState.Active;
    }

    /// <summary>
    /// The player has arrived. Only a registered player can be: a receipt the
    /// desk has not looked at is not a paid spot yet. Whether the session's
    /// check-in window is open is the service's to ask, on the venue's clock.
    /// </summary>
    public void CheckIn(Guid checkedInByUserId, DateTimeOffset now)
    {
        if (!IsRegistered)
        {
            throw new InvalidOperationException("Only a registered player can be checked in.");
        }

        if (IsCheckedIn)
        {
            throw new InvalidOperationException("This player is already checked in.");
        }

        CheckedInAt = now;
        CheckedInByUserId = checkedInByUserId;
        UpdatedAt = now;
    }

    /// <summary>Takes back a check-in made by mistake: the wrong name tapped, the wrong pass scanned.</summary>
    public void UndoCheckIn(DateTimeOffset now)
    {
        if (!IsCheckedIn)
        {
            return;
        }

        CheckedInAt = null;
        CheckedInByUserId = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Whether this registration counts against the session's spots. Works the
    /// same as <see cref="Booking.HoldsTheCourtAt"/>: a receipt stops the clock.
    /// </summary>
    public bool HoldsSpotAt(DateTimeOffset moment) =>
        BookingStatuses.IsLive(Status) && !HasLapsedAt(moment);

    public bool HasLapsedAt(DateTimeOffset moment) =>
        Status == BookingStatus.PendingPayment && ReceiptUrl is null && moment >= HoldsUntil;

    /// <summary>
    /// Records the receipt, and sending it IS the submission, as with a
    /// booking: attaching stops the hold's clock, so it has to put the
    /// registration in the desk's queue at the same moment or it would be
    /// held with nobody looking at it. A second receipt while the desk is
    /// still looking replaces the picture and changes nothing else.
    /// </summary>
    /// <returns>True when this was the first receipt, and the desk has just been handed it.</returns>
    public bool SendReceipt(string receiptUrl, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(receiptUrl))
        {
            throw new ArgumentException("A receipt needs its link.", nameof(receiptUrl));
        }

        if (Status is not (BookingStatus.PendingPayment or BookingStatus.PendingVerification))
        {
            throw new InvalidOperationException("Only a registration waiting to be paid or checked takes a receipt.");
        }

        if (HasLapsedAt(now))
        {
            throw new InvalidOperationException("The hold on this spot has run out.");
        }

        var first = Status == BookingStatus.PendingPayment;

        ReceiptUrl = receiptUrl.Trim();
        ReceiptUploadedAt = now;
        UpdatedAt = now;

        if (first)
        {
            Status = BookingStatus.PendingVerification;
            SubmittedForVerificationAt = now;
        }

        return first;
    }

    /// <summary>
    /// The desk looked at the payment and it is good: the player is registered,
    /// and gets the QR that checks them in at this session.
    /// </summary>
    /// <summary>
    /// How the player pays: a receipt the desk checks, or the payment gateway.
    /// The venue's term when they joined, kept.
    /// </summary>
    public string PaymentChannel { get; private set; } = PaymentMode.Manual;

    public bool IsPaidDirect => PaymentChannel == PaymentMode.Direct;

    /// <summary>Whether a payment that arrived at <paramref name="paidAt"/> registers the player by itself.</summary>
    public bool CanBeConfirmedOnlineBy(DateTimeOffset paidAt) =>
        IsPaidDirect && Status == BookingStatus.PendingPayment && paidAt < HoldsUntil;

    /// <summary>
    /// The payment gateway says the player paid, in time: they are registered
    /// and get their check-in pass, with nobody at the desk asked.
    /// <see cref="ConfirmedByUserId"/> stays empty, which is how the trail
    /// tells this apart from a person's confirmation.
    /// </summary>
    public void ConfirmPaidOnline(DateTimeOffset paidAt, DateTimeOffset now)
    {
        if (!CanBeConfirmedOnlineBy(paidAt))
        {
            throw new InvalidOperationException("Only a registration paid online and in time can confirm itself.");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
        CheckInToken ??= CheckInPass.NewToken();
        UpdatedAt = now;
    }

    public void Confirm(Guid confirmedByUserId, DateTimeOffset now)
    {
        if (Status != BookingStatus.PendingVerification)
        {
            throw new InvalidOperationException("Only a registration waiting on the venue can be confirmed.");
        }

        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
        ConfirmedByUserId = confirmedByUserId;
        CheckInToken ??= CheckInPass.NewToken();
        UpdatedAt = now;
    }

    /// <summary>
    /// The desk looked at the payment and it is not good. The spot is released.
    /// Its own state rather than a cancellation, as with a booking: a player
    /// whose receipt did not add up and one who walked away are different.
    /// </summary>
    public void Reject(string reason, string? note, Guid rejectedByUserId, DateTimeOffset now)
    {
        if (Status != BookingStatus.PendingVerification)
        {
            throw new InvalidOperationException("Only a registration waiting on the venue can be turned down.");
        }

        if (!RejectReason.IsSupported(reason))
        {
            throw new ArgumentException($"'{reason}' is not a reason the desk can give.", nameof(reason));
        }

        Status = BookingStatus.Rejected;
        CancelledAt = now;
        RejectionReason = reason;
        RejectionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RejectedByUserId = rejectedByUserId;
        CancellationReason = RejectReason.Sentence(reason, RejectionNote);
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
