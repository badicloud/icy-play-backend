namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The venue's six-digit code, typed at the desk to check a player in by hand
/// or take a check-in back: anything done without the player's QR.
/// </summary>
public sealed record CheckInCodeRequest(string? Code);

/// <summary>What the desk's camera read off a phone.</summary>
public sealed record CheckInScanRequest(string Scanned);

/// <summary>
/// One registered player on a session's roster.
/// </summary>
/// <param name="FullName">For the desk's own screen.</param>
/// <param name="DisplayName">
/// "Juan D.": for the wide screen on the venue's wall, which anybody walking
/// past can read.
/// </param>
public sealed record CheckInPlayer(
    Guid RegistrationId,
    string FullName,
    string DisplayName,
    DateTimeOffset? CheckedInAt);

/// <summary>
/// One session's door: who is registered, who has arrived, and whether the
/// check-in window is open on the venue's clock.
/// </summary>
public sealed record CheckInRoster(
    Guid OpenPlayId,
    string Title,
    string FacilityName,
    string CourtName,
    string UnitLabel,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    // Venue wall clock.
    DateTime CheckInOpensAt,
    bool IsOpen,
    int MaxPlayers,
    int Registered,
    int CheckedIn,
    IReadOnlyCollection<CheckInPlayer> Players);

/// <summary>What a scan came to. Each reads as one line on the desk's screen.</summary>
public static class CheckInOutcome
{
    public const string CheckedIn = "CheckedIn";
    public const string AlreadyCheckedIn = "AlreadyCheckedIn";
    // A QR of ours, for a registration that is no longer live: cancelled with the session.
    public const string NotRegistered = "NotRegistered";
    // A QR of ours, for another session, another open play or another venue.
    public const string WrongSession = "WrongSession";
    // Looks like a QR of ours, but no registration has that token: made up.
    public const string UnknownPass = "UnknownPass";
    // Not one of our passes at all: a payment QR, a menu, a link.
    public const string NotAPass = "NotAPass";
    public const string WindowClosed = "WindowClosed";
}

/// <param name="Outcome">One of <see cref="CheckInOutcome"/>.</param>
/// <param name="Player">The player the QR belongs to, when it is for this session.</param>
/// <param name="Roster">The session as it stands after the scan, so the screen needs no second request.</param>
public sealed record CheckInScanResult(
    string Outcome,
    string Message,
    CheckInPlayer? Player,
    CheckInRoster Roster);
