namespace IcyPlay.Application.OpenPlays;

/// <summary>What the desk fills in to create or change a draft.</summary>
public sealed record OpenPlayInput(
    Guid BookableCourtId,
    string Title,
    string Level,
    int MaxPlayers,
    decimal RegistrationFee,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyCollection<DayOfWeek> Days,
    DateOnly StartDate,
    DateOnly? EndDate,
    int RegistrationCutoffMinutes,
    OpenPlayEarlyBirdInput? EarlyBird,
    // How long before each session check-in opens. Left out, an hour.
    int? CheckInOpensMinutes = null);

/// <summary>Moves when check-in opens. Allowed after publishing: it is how the venue runs its door.</summary>
public sealed record OpenPlayCheckInWindowInput(int MinutesBeforeStart);

/// <param name="DiscountKind">"Fixed" or "Percentage".</param>
/// <param name="LeadMinutes">How long before the start a player must register to get it.</param>
public sealed record OpenPlayEarlyBirdInput(string DiscountKind, decimal DiscountValue, int LeadMinutes);

/// <summary>One open play as the desk sees it, drafts included.</summary>
public sealed record DeskOpenPlay(
    Guid OpenPlayId,
    Guid FacilityId,
    string FacilityName,
    Guid BookableCourtId,
    Guid CourtId,
    string CourtName,
    string UnitLabel,
    string SportKey,
    string SportName,
    string Title,
    string Level,
    int MaxPlayers,
    decimal RegistrationFee,
    // The platform's top-up from the owner's live contract, added per registration.
    decimal PlatformFee,
    // What a player pays without the early bird.
    decimal Price,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    IReadOnlyCollection<DayOfWeek> Days,
    DateOnly StartDate,
    DateOnly? EndDate,
    int RegistrationCutoffMinutes,
    OpenPlayEarlyBirdInput? EarlyBird,
    // "Draft", "Published" or "Ended".
    string Status,
    bool IsSeeded,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? EndedAt,
    // Registered players: the ones the venue has confirmed.
    int Registrations,
    // The open play's own cover photo. Null when it has none; the public
    // listing then falls back to the court's or the venue's.
    string? CoverPhotoUrl,
    // Receipts sent and waiting on the desk: not registered yet.
    int Waiting,
    int CheckInOpensMinutes,
    // The venue's today, when there is a session today; else null. What the
    // Check in button opens.
    DateOnly? SessionToday,
    // When check-in for today's session opens, on the venue's clock.
    DateTime? CheckInOpensAt,
    // Whether check-in for today's session is open right now.
    bool CheckInOpen);

/// <summary>
/// What the open play form picks from on the admin console: the owner's venues,
/// each with its own today from the server's clock, and their courts. The
/// desk has these already, from its own venues and courts.
/// </summary>
public sealed record OpenPlayPlaces(
    IReadOnlyCollection<Bookings.DeskVenue> Venues,
    IReadOnlyCollection<Bookings.DeskCourt> Courts);

/// <summary>What the browser sends after putting a cover photo into Cloudinary.</summary>
public sealed record OpenPlayPhotoInput(string PublicId, string SecureUrl);

/// <summary>Where open play cover photos go. The desk's upload signature commits to it.</summary>
public static class OpenPlayPhotos
{
    public const string Folder = "icyplay/open-plays";
}

/// <summary>
/// One thing already holding hours the open play wants: a booking, or another
/// published open play on the same part of the floor.
/// </summary>
/// <param name="Kind">"Booking" or "OpenPlay".</param>
/// <param name="Description">The customer's name, or the other open play's title.</param>
public sealed record OpenPlayClash(
    string Kind,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    string Description);

/// <summary>
/// A saved draft, with anything it would clash with if published now. A draft
/// holds nothing, so a clash is only a warning here. Publishing refuses it.
/// </summary>
public sealed record OpenPlaySaved(DeskOpenPlay OpenPlay, IReadOnlyCollection<OpenPlayClash> Clashes);

public enum OpenPlayFailure
{
    None = 0,
    NotFound,
    NotAttended,
    Invalid,
    NotADraft,
    NotPublished,
    Ended,
    Clashes,
    HasRegistrations,
    OutsideOpeningHours,
    StartsInThePast,
    UntrustedPhoto
}

/// <summary>
/// The outcome of a desk action on an open play. On <see cref="OpenPlayFailure.Clashes"/>
/// the clashes say what is in the way; on <see cref="OpenPlayFailure.Invalid"/> and
/// <see cref="OpenPlayFailure.OutsideOpeningHours"/> the message says what is wrong.
/// </summary>
public sealed record OpenPlayResult<T>(
    T? Value,
    OpenPlayFailure Failure,
    string? Message,
    IReadOnlyCollection<OpenPlayClash> Clashes)
{
    public bool Succeeded => Failure == OpenPlayFailure.None;

    public static OpenPlayResult<T> Success(T value) => new(value, OpenPlayFailure.None, null, []);

    public static OpenPlayResult<T> Fail(OpenPlayFailure failure, string? message = null) =>
        new(default, failure, message, []);

    public static OpenPlayResult<T> Clashing(IReadOnlyCollection<OpenPlayClash> clashes) =>
        new(default, OpenPlayFailure.Clashes, null, clashes);
}
