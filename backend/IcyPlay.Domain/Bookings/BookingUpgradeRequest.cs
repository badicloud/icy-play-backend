using IcyPlay.Domain.Common;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Domain.Bookings;

/// <summary>
/// A customer asking to move a booking onto hours that cost more than the ones
/// they hold, and offering to pay the difference.
///
/// Moving a booking is otherwise immediate: the customer picks a court and it
/// happens, because asking a venue to approve every change put people in a
/// queue nobody was answering. This exists for the one case that cannot be
/// instant — money has to change hands, and the venue has to see it arrive
/// before it gives up the better court.
///
/// Only upgrades come through here. A move to the same price or cheaper does
/// not create one of these at all.
/// </summary>
public sealed class BookingUpgradeRequest : Entity
{
    private BookingUpgradeRequest()
    {
    }

    public BookingUpgradeRequest(
        Guid bookingId,
        Guid toBookableCourtId,
        string toCourtName,
        Guid requestedByUserId,
        decimal rentalNow,
        decimal rentalNew,
        int holdMinutes,
        string moveReason,
        string? moveReasonNote,
        DateTimeOffset requestedAt)
    {
        BookingId = bookingId;
        MoveReason = moveReason;
        MoveReasonNote = string.IsNullOrWhiteSpace(moveReasonNote) ? null : moveReasonNote.Trim();
        ToBookableCourtId = toBookableCourtId;
        ToCourtName = toCourtName.Trim();
        RequestedByUserId = requestedByUserId;
        RentalNow = rentalNow;
        RentalNew = rentalNew;

        // Fixed when the request is made, not worked out again when it is paid.
        // A rate the venue changes in between must not change what somebody has
        // already been asked for.
        BalanceDue = Math.Max(0m, rentalNew - rentalNow);

        HoldsUntil = requestedAt.AddMinutes(PaymentHold.Clamp(holdMinutes));
        Status = UpgradeStatus.AwaitingPayment;
        CreatedAt = requestedAt;
    }

    public Guid BookingId
    {
        get; private set;
    }
    public Booking Booking { get; private set; } = null!;

    public Guid ToBookableCourtId
    {
        get; private set;
    }
    public BookableCourt ToBookableCourt { get; private set; } = null!;

    /// <summary>
    /// What the court was called when the upgrade was asked for. The same
    /// reason the booking snapshots its own: a rename must not rewrite what
    /// somebody was told they were paying for.
    /// </summary>
    public string ToCourtName { get; private set; } = string.Empty;

    /// <summary>
    /// The hours being asked for, priced as they stood at the time.
    ///
    /// Held here rather than worked out again at approval, because the customer
    /// pays against this figure. A venue that reprices its court between the
    /// request and the approval must not change what has already been paid.
    /// </summary>
    public ICollection<BookingUpgradeSlot> Slots { get; private set; } = [];

    public Guid RequestedByUserId
    {
        get; private set;
    }

    /// <summary>
    /// Why the customer wants to move, asked when they ask for the upgrade.
    ///
    /// Carried here rather than asked again at approval: by then the customer
    /// has paid and gone, and the desk approving it is not the one who knows
    /// why. Null only on requests made before customers were asked.
    /// </summary>
    public string? MoveReason
    {
        get; private set;
    }

    public string? MoveReasonNote
    {
        get; private set;
    }

    /// <summary>What the booking's hours come to now, in court rental alone.</summary>
    public decimal RentalNow
    {
        get; private set;
    }

    /// <summary>What the asked-for hours come to, in court rental alone.</summary>
    public decimal RentalNew
    {
        get; private set;
    }

    /// <summary>
    /// The difference, and never less than nothing.
    ///
    /// Court rental on both sides. The platform fee is charged per hour booked
    /// and an upgrade buys no hours — the same number of them end up somewhere
    /// else — so counting it would put a price on a move that costs nothing.
    /// </summary>
    public decimal BalanceDue
    {
        get; private set;
    }

    public string Status { get; private set; } = UpgradeStatus.AwaitingPayment;

    /// <summary>
    /// When the hours being asked for go back on sale.
    ///
    /// Stops mattering once a receipt is attached, exactly as a booking's own
    /// hold does: from that moment the customer has done their part and the
    /// wait is the venue's.
    /// </summary>
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

    public Guid? SettledByUserId
    {
        get; private set;
    }

    public DateTimeOffset? SettledAt
    {
        get; private set;
    }

    /// <summary>Why the venue said no. The customer is told.</summary>
    public string? DeclineReason
    {
        get; private set;
    }

    /// <summary>Whether this is still waiting on somebody.</summary>
    public bool IsOpen =>
        Status is UpgradeStatus.AwaitingPayment or UpgradeStatus.AwaitingApproval;

    /// <summary>
    /// Whether it still holds the hours it is asking for.
    ///
    /// An unpaid request lets go when its clock runs out, the same way an
    /// unpaid booking does. Once the receipt is in, it holds them until the
    /// venue answers — the customer cannot be blamed for a queue.
    /// </summary>
    public bool HoldsTheCourtAt(DateTimeOffset moment) =>
        IsOpen && (Status != UpgradeStatus.AwaitingPayment || ReceiptUrl is not null || moment < HoldsUntil);

    public void AttachReceipt(string receiptUrl, DateTimeOffset now)
    {
        ReceiptUrl = receiptUrl.Trim();
        ReceiptUploadedAt = now;
        UpdatedAt = now;
    }

    /// <summary>The customer has sent the payment. It is the venue's turn.</summary>
    public void Submit(DateTimeOffset now)
    {
        Status = UpgradeStatus.AwaitingApproval;
        UpdatedAt = now;
    }

    /// <summary>The venue has seen the payment. The booking moves.</summary>
    public void Approve(Guid attendantUserId, DateTimeOffset now)
    {
        Status = UpgradeStatus.Approved;
        SettledByUserId = attendantUserId;
        SettledAt = now;
        UpdatedAt = now;
    }

    /// <summary>The venue says no. The booking stays exactly where it was.</summary>
    public void Decline(Guid attendantUserId, string? reason, DateTimeOffset now)
    {
        Status = UpgradeStatus.Declined;
        SettledByUserId = attendantUserId;
        SettledAt = now;
        DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = now;
    }

    /// <summary>The customer thought better of it before paying.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        Status = UpgradeStatus.Withdrawn;
        UpdatedAt = now;
    }

    /// <summary>The clock ran out with no receipt. The hours go back on sale.</summary>
    public void Expire(DateTimeOffset now)
    {
        Status = UpgradeStatus.Expired;
        UpdatedAt = now;
    }
}

/// <summary>
/// One hour an upgrade is asking for, at the price it was quoted.
///
/// A copy rather than a reference to the booking's own slots: these are hours
/// the booking does not hold yet, and may never hold.
/// </summary>
public sealed class BookingUpgradeSlot : Entity
{
    private BookingUpgradeSlot()
    {
    }

    public BookingUpgradeSlot(
        Guid requestId,
        DateOnly date,
        TimeOnly startsAt,
        TimeOnly endsAt,
        CourtRateKind rateKind,
        decimal amount,
        decimal platformFee,
        DateTimeOffset createdAt)
    {
        RequestId = requestId;
        Date = date;
        StartsAt = startsAt;
        EndsAt = endsAt;
        RateKind = rateKind;
        Amount = amount;
        PlatformFee = platformFee;
        CreatedAt = createdAt;
    }

    public Guid RequestId
    {
        get; private set;
    }
    public BookingUpgradeRequest Request { get; private set; } = null!;

    public DateOnly Date
    {
        get; private set;
    }

    public TimeOnly StartsAt
    {
        get; private set;
    }

    public TimeOnly EndsAt
    {
        get; private set;
    }

    public CourtRateKind RateKind
    {
        get; private set;
    }

    /// <summary>The court rental for this hour.</summary>
    public decimal Amount
    {
        get; private set;
    }

    /// <summary>
    /// The platform's share of this hour. Carried so the slot the booking ends
    /// up with is a complete one, not so it is charged again on the upgrade.
    /// </summary>
    public decimal PlatformFee
    {
        get; private set;
    }
}

public static class UpgradeStatus
{
    /// <summary>Asked for, and waiting on the customer's money.</summary>
    public const string AwaitingPayment = "AwaitingPayment";

    /// <summary>Paid, and waiting on somebody at the venue to look.</summary>
    public const string AwaitingApproval = "AwaitingApproval";

    public const string Approved = "Approved";
    public const string Declined = "Declined";
    public const string Withdrawn = "Withdrawn";
    public const string Expired = "Expired";

    public static readonly IReadOnlyCollection<string> All =
        [AwaitingPayment, AwaitingApproval, Approved, Declined, Withdrawn, Expired];

    public static bool IsSupported(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}
