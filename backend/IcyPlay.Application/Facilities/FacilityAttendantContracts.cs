using FluentValidation;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// Somebody who works a venue's desk.
///
/// The owner appears in this list too, marked as such and without an id: they
/// attend every venue they own by virtue of owning it, and a console that left
/// them out would read as though nobody could confirm a booking.
/// </summary>
public sealed record FacilityAttendantDetail(
    /// <summary>Null for the owner, who is not a row.</summary>
    Guid? Id,
    Guid UserId,
    string FullName,
    string Email,
    string? PhoneNumber,
    /// <summary>True for the owner, who cannot be removed from their own venue.</summary>
    bool IsOwner,
    /// <summary>Whether they have set a password and taken over the account.</summary>
    bool HasAccepted,
    /// <summary>
    /// False when the address already had an IcyPlay account: they were put on
    /// the desk without an invitation, because they already have a password.
    ///
    /// Sent so the console can say so. "Active" with nothing else beside it
    /// reads as "we emailed them and they accepted", and nobody emailed them.
    /// </summary>
    bool WasInvited,
    /// <summary>
    /// How many invitations have gone to them, the first one included. Two or
    /// more means somebody has already pressed resend, which is what an admin
    /// wondering whether to press it again needs to know.
    /// </summary>
    int InvitationsSent,
    /// <summary>When the newest of those was issued.</summary>
    DateTimeOffset? LastInvitedAt,
    DateTimeOffset? AddedAt,
    /// <summary>
    /// Whether they may read the venue's money reports. Always for the owner;
    /// for an attendant, whatever the owner set on their desk.
    /// </summary>
    bool CanSeeMoney = false);

public sealed record InviteAttendantRequest(
    string FullName,
    string Email,
    string? PhoneNumber,
    string? Reason);

public enum AttendantFailure
{
    None,
    FacilityNotFound,
    /// <summary>Already on this venue's desk.</summary>
    AlreadyAttending,
    /// <summary>The owner attends their own venue and cannot be added or removed.</summary>
    IsTheOwner,
    AttendantNotFound,
    /// <summary>
    /// They have already set their password, so there is no invitation left to
    /// send. A fresh link would be an offer to replace a password they are
    /// using, which is not what an admin pressing "resend" is asking for.
    /// </summary>
    InvitationAlreadyAccepted,
    /// <summary>
    /// Somebody already holds an IcyPlay account at that address.
    ///
    /// One address, one account, with no exception: an admin typing an address
    /// that is already taken is naming somebody the console cannot show them,
    /// so it is refused rather than quietly handed a desk and the bookings
    /// that come with it. Somebody taken off this desk stays off it — their
    /// address has an account now, like anybody else's.
    /// </summary>
    EmailAlreadyRegistered
}

/// <summary>
/// What the console may do with an address before it is submitted.
///
/// Strings rather than an enum, because these cross the wire to a browser and
/// a number there says nothing to anybody reading it.
/// </summary>
public static class AttendantEmailStatus
{
    /// <summary>Nobody holds it. They can be invited.</summary>
    public const string Available = "Available";

    /// <summary>Already on this venue's desk today.</summary>
    public const string AlreadyAttending = "AlreadyAttending";

    /// <summary>The owner's own address. They attend it by owning it.</summary>
    public const string IsTheOwner = "IsTheOwner";

    /// <summary>
    /// Somebody already holds an account at it, including somebody who worked
    /// this venue before and was taken off it. It cannot be used again.
    /// </summary>
    public const string AlreadyRegistered = "AlreadyRegistered";
}

/// <summary>
/// What the console learns about an address as it is typed, so an admin is
/// told before they press save rather than after.
/// </summary>
public sealed record AttendantEmailCheck(
    string Email,
    string Status,
    /// <summary>The name on the account, when there is one, so the console can say who it means.</summary>
    string? FullName)
{
    /// <summary>Only an address nobody holds.</summary>
    public bool CanBeAdded => Status == AttendantEmailStatus.Available;
}

public sealed record AttendantResult<T>(T? Value, AttendantFailure Failure = AttendantFailure.None)
{
    public bool Succeeded => Failure == AttendantFailure.None;
    public static AttendantResult<T> Success(T value) => new(value);
    public static AttendantResult<T> Fail(AttendantFailure failure) => new(default, failure);
}

public sealed class InviteAttendantRequestValidator : AbstractValidator<InviteAttendantRequest>
{
    public InviteAttendantRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
    }
}
