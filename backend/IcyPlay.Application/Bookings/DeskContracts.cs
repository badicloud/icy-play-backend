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
    UnknownTab
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
