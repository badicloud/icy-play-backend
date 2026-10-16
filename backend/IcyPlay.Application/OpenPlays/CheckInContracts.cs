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
/// <param name="RegistrationId">The registration, or the free player's own id when <paramref name="IsFree"/>.</param>
/// <param name="IsFree">Put in for free by the venue, not registered and paid.</param>
/// <param name="Mobile">A free player's number, for the desk. Never a registered player's.</param>
/// <param name="Lineup">Where they are in the matches; null until they have checked in.</param>
public sealed record CheckInPlayer(
    Guid RegistrationId,
    string FullName,
    string DisplayName,
    DateTimeOffset? CheckedInAt,
    bool IsFree = false,
    string? Mobile = null,
    CheckInLineup? Lineup = null);

/// <summary>A checked-in player's place in the session's matches, as the matches page has it.</summary>
/// <param name="State">"Playing", "InMatch" (in a match still to come), "Waiting" or "Done".</param>
/// <param name="MatchNumber">The match they are in, when playing or in one to come.</param>
/// <param name="Played">Finished matches they were on court in.</param>
/// <param name="HasPlayed">On court in a match that started: they stay on record, so cannot be removed.</param>
public sealed record CheckInLineup(string State, int? MatchNumber, int Played, bool HasPlayed);

/// <summary>A player the desk puts into a session for free. The mobile number is optional.</summary>
public sealed record AddFreePlayerRequest(string? FullName, string? Mobile);

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
    IReadOnlyCollection<CheckInPlayer> Players,
    // How many the desk may put in for free, and how many it has.
    int FreeSpots = 0,
    int FreePlayers = 0,
    // Whether one more can be put in for free right now: a free spot and a
    // spot left, before the session ends. The server's answer, not the page's.
    bool CanAddFreePlayer = false);

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
