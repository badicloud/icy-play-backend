using FluentValidation;

namespace IcyPlay.Application.Bookings;

/// <summary>
/// One booking as the venue's desk sees it.
///
/// Not <see cref="BookingDetail"/>, which is what a customer sees of their own:
/// this carries who booked it, because checking a GCash receipt means checking
/// the name on it against the person who sent it.
/// </summary>
public sealed record DeskBooking(
    Guid Id,
    Guid FacilityId,
    string FacilityName,
    /// <summary>The floor it was sold on, for grouping a venue's own diary.</summary>
    Guid CourtId,
    /// <summary>The part of that floor. What a second booking at the same hour is not.</summary>
    Guid BookableCourtId,
    int DivisionNumber,
    string CourtName,
    string SportName,
    /// <summary>The sport's stable key, for artwork.</summary>
    string SportKey,
    string Kind,
    string Status,
    string CustomerName,
    string CustomerEmail,
    string? CustomerPhone,
    DateOnly StartDate,
    DateOnly EndDate,
    int BookedHours,
    decimal RentalTotal,
    decimal PlatformFeeTotal,
    decimal Total,
    /// <summary>The receipt the customer sent. The whole point of the page.</summary>
    string? ReceiptUrl,
    DateTimeOffset? ReceiptUploadedAt,
    DateTimeOffset? SubmittedForVerificationAt,
    DateTimeOffset? ConfirmedAt,
    /// <summary>Why it was turned down, when it was.</summary>
    string? DecisionReason,
    IReadOnlyCollection<BookedSlot> Slots,
    DateTimeOffset CreatedAt);

/// <summary>A venue the signed-in person may confirm bookings for.</summary>
public sealed record DeskVenue(Guid Id, string Name);

/// <summary>
/// A court as the venue registered it, with the parts it is sold in.
///
/// The desk's diary is kept per court rather than per part: the court is the
/// thing somebody walks onto and unlocks, and a floor marked out three ways for
/// pickleball is still one floor to the person standing at it.
/// </summary>
public sealed record DeskCourt(
    Guid Id,
    Guid FacilityId,
    string FacilityName,
    string Name,
    IReadOnlyCollection<DeskCourtUnit> Units);

/// <summary>One thing that can be booked on a court: a sport, and which part.</summary>
public sealed record DeskCourtUnit(
    Guid BookableCourtId,
    string SportName,
    string SportKey,
    int DivisionNumber,
    /// <summary>
    /// What to call it on screen: "Basketball" for a whole floor, "Pickleball 2"
    /// for one of three. Derived rather than stored, like every other name for
    /// a division.
    /// </summary>
    string Label);

/// <summary>
/// One booked hour, as the diary shows it.
///
/// Deliberately thin. A month of one court is some five hundred hours, and
/// sending each one a customer's address, a receipt and a list of rates would
/// be sending far more than the page can show. The rest arrives when somebody
/// clicks an hour.
/// </summary>
public sealed record ScheduleEntry(
    Guid BookingId,
    Guid CourtId,
    Guid BookableCourtId,
    string UnitLabel,
    string SportKey,
    string Status,
    string CustomerName,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt);

/// <summary>
/// What the diary is being asked for.
///
/// <paramref name="Status"/> is null for everything still standing — held, paid
/// and waiting, or confirmed. A named status is for the list, which is where a
/// venue goes looking for what fell through.
/// </summary>
public sealed record CourtBookingQuery(
    Guid CourtId,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 10);

/// <summary>
/// Which pile the desk is looking at.
///
/// Two tabs rather than one list: what is waiting is work, and what is confirmed
/// is a record. Mixing them buries the three that need doing under the fifty
/// that are done.
/// </summary>
public static class DeskTab
{
    /// <summary>Paid and handed over, waiting on somebody at the venue.</summary>
    public const string Waiting = "Waiting";

    /// <summary>Checked and confirmed. History.</summary>
    public const string Confirmed = "Confirmed";

    public static readonly IReadOnlyCollection<string> All = [Waiting, Confirmed];

    public static bool IsSupported(string value) => All.Contains(value);
}

public sealed record DeskQuery(
    string Tab = DeskTab.Waiting,
    /// <summary>Null means every venue this person works.</summary>
    Guid? FacilityId = null,
    int Page = 1,
    int PageSize = 10);

/// <summary>
/// The two dials a venue sets for itself, both about how long it is prepared
/// to hold a court for somebody who has not paid yet.
/// </summary>
public sealed record DeskSettings(
    /// <summary>Minutes a court is held while the customer pays.</summary>
    int PartialBookingExpiryMinutes,
    /// <summary>How many times a customer may move one booking.</summary>
    int MoveLimit,
    int SmallestExpiry,
    int LargestExpiry,
    int SmallestMoveLimit,
    int LargestMoveLimit);

public sealed record UpdateDeskSettingsRequest(
    int PartialBookingExpiryMinutes,
    int MoveLimit);

public sealed record RejectBookingRequest(string? Reason);

public enum DeskFailure
{
    None,
    /// <summary>They do not work this venue, or it is not there. Same answer.</summary>
    NotAttended,
    BookingNotFound,
    /// <summary>Already decided. A second press must not undo the first.</summary>
    NotWaiting,
    /// <summary>Nothing to look at, so nothing to confirm.</summary>
    NoReceipt,
    UnknownTab,
    /// <summary>Not a status anybody can ask for.</summary>
    UnknownStatus,
    /// <summary>More days than a diary will answer for in one go.</summary>
    WindowTooWide
}

public sealed record DeskResult<T>(T? Value, DeskFailure Failure = DeskFailure.None)
{
    public bool Succeeded => Failure == DeskFailure.None;
    public static DeskResult<T> Success(T value) => new(value);
    public static DeskResult<T> Fail(DeskFailure failure) => new(default, failure);
}

public sealed class RejectBookingRequestValidator : AbstractValidator<RejectBookingRequest>
{
    public RejectBookingRequestValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
