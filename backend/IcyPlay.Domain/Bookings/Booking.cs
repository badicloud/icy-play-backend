using IcyPlay.Domain.Common;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Domain.Bookings;

/// <summary>
/// One customer's hold on one bookable court, for one or more hours.
///
/// Almost everything on it is a SNAPSHOT. A booking is a record of an agreement
/// made at a moment: what the court was called, what it cost, what the platform
/// charged. Renaming the court or re-pricing the sport a week later must not
/// reach backwards and rewrite what somebody already agreed to pay — which is
/// the opposite of how the catalogue works, where a rename is supposed to flow
/// through everywhere.
/// </summary>
public sealed class Booking : Entity
{
    private Booking()
    {
    }

    public Booking(
        Guid bookableCourtId,
        Guid customerUserId,
        string kind,
        string courtName,
        string facilityName,
        string sportName,
        decimal platformHourlyRate,
        DateOnly startDate,
        DateOnly endDate,
        int holdMinutes,
        DateTimeOffset createdAt)
    {
        BookableCourtId = bookableCourtId;
        CustomerUserId = customerUserId;
        Kind = kind;
        CourtName = courtName;
        FacilityName = facilityName;
        SportName = sportName;
        PlatformHourlyRate = platformHourlyRate;
        StartDate = startDate;
        EndDate = endDate;
        HoldsUntil = createdAt.AddMinutes(PaymentHold.Clamp(holdMinutes));
        Status = BookingStatus.PendingPayment;
        CreatedAt = createdAt;
    }

    public Guid BookableCourtId
    {
        get; private set;
    }
    public BookableCourt BookableCourt { get; private set; } = null!;

    public Guid CustomerUserId
    {
        get; private set;
    }

    public BookingStatus Status { get; private set; } = BookingStatus.PendingPayment;

    /// <summary>
    /// Hourly, a whole day, or a run of whole days.
    ///
    /// Stored rather than worked out from the slots, for the same reason the
    /// rest of this is: a whole day means "every hour the court was open THAT
    /// day", and the day the venue shortens its hours, a booking that was a
    /// whole day would start reading as a handful of hours.
    /// </summary>
    public string Kind { get; private set; } = BookingKind.Hourly;

    /// <summary>What was booked, as it was named then.</summary>
    public string CourtName { get; private set; } = string.Empty;
    public string FacilityName { get; private set; } = string.Empty;
    public string SportName { get; private set; } = string.Empty;

    /// <summary>
    /// The owner's platform rate at the time of booking, so a later change to
    /// their contract cannot restate an invoice that has already gone out.
    /// </summary>
    public decimal PlatformHourlyRate
    {
        get; private set;
    }

    /// <summary>
    /// The first and last date booked. Same value when it is one day.
    ///
    /// Derivable from the slots, and stored anyway — the exception here rather
    /// than the rule. A venue's console asks "what is booked this week" far more
    /// often than it asks about any one booking, and that question should be an
    /// indexed range rather than a walk through every hour of every booking.
    /// </summary>
    public DateOnly StartDate
    {
        get; private set;
    }
    public DateOnly EndDate
    {
        get; private set;
    }

    /// <summary>
    /// When an unpaid hold lets go of the court.
    ///
    /// Nothing sweeps expired bookings: the status stays PendingPayment and the
    /// hold simply stops counting, the way a lapsed contract stops making an
    /// owner bookable. A background job would be a second thing to keep running
    /// and a second answer to the same question.
    /// </summary>
    public DateTimeOffset HoldsUntil
    {
        get; private set;
    }

    /// <summary>The GCash receipt, once the customer has sent one.</summary>
    public string? ReceiptUrl
    {
        get; private set;
    }
    public DateTimeOffset? ReceiptUploadedAt
    {
        get; private set;
    }

    /// <summary>When the customer said they had paid and asked the venue to check.</summary>
    public DateTimeOffset? SubmittedForVerificationAt
    {
        get; private set;
    }

    public ICollection<BookingSlot> Slots { get; private set; } = [];

    /// <summary>
    /// How many times these hours have been carried to another date.
    /// </summary>
    public int MoveCount
    {
        get; private set;
    }

    public DateTimeOffset? MovedAt
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

    /// <summary>Hours booked. One per slot, since a slot is an hour of court time.</summary>
    public int BookedHours => Slots.Count;

    /// <summary>What the venue is owed for the court itself.</summary>
    public decimal RentalTotal => Slots.Sum(slot => slot.Amount);

    /// <summary>
    /// What the customer pays on top, which the venue then owes the platform.
    /// See docs/platform-fee-strategy.md: the customer pays the venue
    /// `rental + fee` directly, and the platform bills the venue the fee.
    /// </summary>
    public decimal PlatformFeeTotal => Slots.Sum(slot => slot.PlatformFee);

    public decimal Total => RentalTotal + PlatformFeeTotal;

    /// <summary>
    /// Whether this booking still holds its court at a given moment.
    ///
    /// A rejected or cancelled one has let go. So has an unpaid one whose hold
    /// has run out — which is why this takes the time rather than reading it
    /// from a status somebody has to remember to write.
    ///
    /// **A receipt stops the clock.** Not the submit button: between uploading
    /// and pressing it there is nothing left to do, and somebody who uploaded at
    /// minute twenty-nine must not lose a court they have already paid for
    /// because they read the page for two minutes first.
    /// </summary>
    public bool HoldsTheCourtAt(DateTimeOffset moment) =>
        BookingStatuses.IsLive(Status) && !HasLapsedAt(moment);

    /// <summary>True once the hold has run out with no receipt sent.</summary>
    public bool HasLapsedAt(DateTimeOffset moment) =>
        Status == BookingStatus.PendingPayment && ReceiptUrl is null && moment >= HoldsUntil;

    public void AddSlot(BookingSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        Slots.Add(slot);
    }

    /// <summary>
    /// Records the receipt the customer sent. Does not confirm anything — a
    /// person at the venue still has to look at it.
    /// </summary>
    public void AttachReceipt(string receiptUrl, DateTimeOffset now)
    {
        ReceiptUrl = receiptUrl;
        ReceiptUploadedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Hands the booking to the venue to check.
    ///
    /// The hold stops mattering from here: somebody who has paid must not lose
    /// their court because the venue was asleep when the clock ran out.
    /// </summary>
    public void SubmitForVerification(DateTimeOffset now)
    {
        Status = BookingStatus.PendingVerification;
        SubmittedForVerificationAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Somebody at the venue has looked at the payment and it is good.
    /// </summary>
    public void Confirm(DateTimeOffset now)
    {
        Status = BookingStatus.Confirmed;
        ConfirmedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Somebody at the venue has looked at the payment and it is not.
    ///
    /// Its own state rather than a cancellation: a customer who changed their
    /// mind and a receipt that did not add up are different things, and a venue
    /// reading its own history has to be able to tell them apart. The court
    /// goes back on sale either way, because neither still holds it.
    ///
    /// The moment lands on <see cref="CancelledAt"/>, which is when the booking
    /// stopped standing whichever way it stopped.
    /// </summary>
    public void Reject(string? reason, DateTimeOffset now)
    {
        Status = BookingStatus.Rejected;
        CancelledAt = now;
        CancellationReason = reason;
        UpdatedAt = now;
    }

    /// <summary>
    /// Carries the whole booking to another date, keeping its hours.
    /// </summary>
    /// <remarks>
    /// The caller has already priced the new hours and checked they are free.
    /// What this does is swap the slots and remember that it happened, because
    /// the count is the only thing standing between a booking and an indefinite
    /// option on somebody else's calendar.
    /// </remarks>
    public void MoveTo(
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyCollection<BookingSlot> slots,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(slots);

        StartDate = startDate;
        EndDate = endDate;
        Slots.Clear();

        foreach (var slot in slots)
        {
            Slots.Add(slot);
        }

        MoveCount += 1;
        MovedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(string? reason, DateTimeOffset now)
    {
        Status = BookingStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = reason;
        UpdatedAt = now;
    }
}

/// <summary>
/// How the hours were chosen. Strings rather than an enum, matching
/// <see cref="ActivityKind"/> and <see cref="BookableCourtKind"/>.
/// </summary>
public static class BookingKind
{
    /// <summary>Hours picked one at a time. They need not run back to back.</summary>
    public const string Hourly = "Hourly";

    /// <summary>Every hour the court is open on one date.</summary>
    public const string WholeDay = "WholeDay";

    /// <summary>Every hour the court is open across a run of consecutive dates.</summary>
    public const string MultiDay = "MultiDay";

    public static readonly IReadOnlyCollection<string> All = [Hourly, WholeDay, MultiDay];

    public static bool IsSupported(string value) => All.Contains(value);

    /// <summary>Whole days are sold entire, so a single taken hour blocks them.</summary>
    public static bool TakesWholeDays(string value) => value is WholeDay or MultiDay;
}

/// <summary>
/// Which statuses still hold a court. The enum carries the states; this carries
/// the one question the rest of the system asks about them, so "is this hour
/// free" is answered the same way everywhere it is asked.
/// </summary>
/// <summary>
/// What a booking may still have done to it, and when.
///
/// The limits are here rather than in the service because they are the deal, not
/// an implementation detail: three moves, nothing inside the last day, and only
/// onto the same kind of day so the price cannot change underneath anybody.
/// </summary>
public static class BookingMove
{
    /// <summary>
    /// Three, then no more. A booking that can be carried forward for ever is
    /// an option on a venue's calendar rather than a booking, and the venue is
    /// the one turning other people away to keep holding it.
    /// </summary>
    public const int Limit = 3;

    /// <summary>
    /// A venue that has kept an hour free needs more notice than an afternoon.
    /// Counted to the start of the booking as it stands, on the venue's clock.
    /// </summary>
    public const int NoticeInHours = 24;

    /// <summary>
    /// Saturdays and Sundays are their own kind of day, and so are holidays: a
    /// court is priced differently on each. Moving within a kind is what keeps
    /// the total identical, which is what lets a move happen with no second
    /// payment and no refund.
    /// </summary>
    public static bool SameKindOfDay(DateOnly from, DateOnly to, bool fromIsHoliday, bool toIsHoliday) =>
        fromIsHoliday == toIsHoliday
        && Court.IsWeekend(from.DayOfWeek) == Court.IsWeekend(to.DayOfWeek);
}

public static class BookingStatuses
{
    public static readonly IReadOnlyCollection<BookingStatus> Live =
    [
        BookingStatus.PendingPayment,
        BookingStatus.PendingVerification,
        BookingStatus.Confirmed
    ];

    public static bool IsLive(BookingStatus status) => Live.Contains(status);
}
