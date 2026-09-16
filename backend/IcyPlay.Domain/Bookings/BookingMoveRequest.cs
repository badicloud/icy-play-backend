using IcyPlay.Domain.Common;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Domain.Bookings;

/// <summary>
/// A booking asking to be played somewhere else.
///
/// Kept apart from the booking rather than done to it. A move can want paying
/// for, and anything waiting to be paid for needs a clock — but putting a
/// confirmed booking back into a paying state would let that clock expire a
/// booking the customer has already settled. So the request holds the new court
/// and the balance, and the booking is not touched until the move completes.
///
/// Every way this can end — paid and confirmed, declined, abandoned, run out of
/// time — leaves the booking exactly where it was.
/// </summary>
public sealed class BookingMoveRequest : Entity
{
    private BookingMoveRequest()
    {
    }

    public BookingMoveRequest(
        Guid bookingId,
        Guid toBookableCourtId,
        string toCourtName,
        string initiator,
        Guid requestedByUserId,
        string? reason,
        decimal paidBefore,
        decimal newTotal,
        int holdMinutes,
        DateTimeOffset requestedAt)
    {
        BookingId = bookingId;
        ToBookableCourtId = toBookableCourtId;
        ToCourtName = toCourtName.Trim();
        Initiator = initiator.Trim();
        RequestedByUserId = requestedByUserId;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        PaidBefore = paidBefore;
        NewTotal = newTotal;

        // Never below zero. A cheaper court is not a credit — the booking keeps
        // what it paid, and the customer owes nothing further.
        BalanceDue = Math.Max(0m, newTotal - paidBefore);

        // A move with nothing to pay has nobody to wait for but the venue, and
        // a clock on the venue's own queue would expire a request because
        // somebody was at lunch.
        Status = BalanceDue > 0m
            ? MoveRequestStatus.AwaitingPayment
            : MoveRequestStatus.AwaitingConfirmation;

        HoldsUntil = requestedAt.AddMinutes(PaymentHold.Clamp(holdMinutes));
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
    /// What the court was called when the move was asked for. The same reason
    /// the booking snapshots its court: a rename must not rewrite what somebody
    /// was told they were moving to.
    /// </summary>
    public string ToCourtName { get; private set; } = string.Empty;

    public string Initiator { get; private set; } = MoveInitiator.Customer;

    public Guid RequestedByUserId
    {
        get; private set;
    }

    /// <summary>
    /// Why the booking is being moved. Required of an attendant moving somebody
    /// else's booking — a court taken away from a customer without a reason on
    /// the record is a complaint nobody can answer later.
    /// </summary>
    public string? Reason
    {
        get; private set;
    }

    /// <summary>What the booking had already been settled for when this was asked.</summary>
    public decimal PaidBefore
    {
        get; private set;
    }

    /// <summary>What the booking would come to on the new court.</summary>
    public decimal NewTotal
    {
        get; private set;
    }

    /// <summary>The shortfall, and never less than nothing.</summary>
    public decimal BalanceDue
    {
        get; private set;
    }

    public string Status { get; private set; } = MoveRequestStatus.AwaitingPayment;

    /// <summary>
    /// When the held court goes back on sale. Stops mattering once a receipt is
    /// attached, exactly as a booking's own hold does: from that moment the
    /// customer has done their part and the wait is the venue's.
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

    /// <summary>Set when somebody at the venue decided the customer should not pay.</summary>
    public Guid? WaivedByUserId
    {
        get; private set;
    }

    /// <summary>
    /// Why the payment was let off, which is a different question from why the
    /// booking was moved. A flooded court explains the move; it does not
    /// explain who decided to absorb the difference.
    /// </summary>
    public string? WaiverReason
    {
        get; private set;
    }

    public string? DeclineReason
    {
        get; private set;
    }

    public bool IsOpen =>
        Status is MoveRequestStatus.AwaitingPayment or MoveRequestStatus.AwaitingConfirmation;

    /// <summary>
    /// Whether this still holds the new court's hours at the given moment.
    ///
    /// The same shape as a booking's own hold: an unpaid request past its time
    /// with no receipt against it is holding nothing, whatever the stored status
    /// says. Read this rather than the status when asking what is taken.
    /// </summary>
    public bool HoldsTheCourtAt(DateTimeOffset moment) =>
        IsOpen && (Status != MoveRequestStatus.AwaitingPayment || ReceiptUrl is not null || moment < HoldsUntil);

    public void AttachReceipt(string receiptUrl, DateTimeOffset now)
    {
        ReceiptUrl = receiptUrl.Trim();
        ReceiptUploadedAt = now;
        Status = MoveRequestStatus.AwaitingConfirmation;
        UpdatedAt = now;
    }

    /// <summary>
    /// Somebody at the venue decided the customer should not pay the
    /// difference. The move then needs only confirming.
    /// </summary>
    public void Waive(Guid waivedByUserId, string reason, DateTimeOffset now)
    {
        BalanceDue = 0m;
        WaivedByUserId = waivedByUserId;
        WaiverReason = reason.Trim();
        Status = MoveRequestStatus.AwaitingConfirmation;
        UpdatedAt = now;
    }

    public void Complete(Guid settledByUserId, DateTimeOffset now)
    {
        Status = MoveRequestStatus.Completed;
        SettledByUserId = settledByUserId;
        SettledAt = now;
        UpdatedAt = now;
    }

    public void Decline(Guid settledByUserId, string? reason, DateTimeOffset now)
    {
        Status = MoveRequestStatus.Declined;
        DeclineReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SettledByUserId = settledByUserId;
        SettledAt = now;
        UpdatedAt = now;
    }

    /// <summary>The customer thought better of it before paying.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        Status = MoveRequestStatus.Withdrawn;
        UpdatedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        Status = MoveRequestStatus.Expired;
        UpdatedAt = now;
    }
}

/// <summary>
/// Who asked for the move, which decides what the system may do without saying
/// anything: a customer upgrading themselves is a purchase, and a venue moving
/// somebody is not.
/// </summary>
public static class MoveInitiator
{
    public const string Customer = "Customer";
    public const string Attendant = "Attendant";

    public static readonly IReadOnlyCollection<string> All = [Customer, Attendant];

    public static bool IsSupported(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}

public static class MoveRequestStatus
{
    /// <summary>Waiting on the customer, with the new court held and a clock running.</summary>
    public const string AwaitingPayment = "AwaitingPayment";

    /// <summary>Waiting on the venue. Nothing expires here.</summary>
    public const string AwaitingConfirmation = "AwaitingConfirmation";

    public const string Completed = "Completed";
    public const string Declined = "Declined";
    public const string Withdrawn = "Withdrawn";
    public const string Expired = "Expired";

    public static readonly IReadOnlyCollection<string> All =
        [AwaitingPayment, AwaitingConfirmation, Completed, Declined, Withdrawn, Expired];
}
