namespace IcyPlay.Application.OpenPlays;

/// <summary>One player on the matches board.</summary>
/// <param name="ParticipantId">Their place in the session's line: what Done and Back in take.</param>
/// <param name="DisplayName">"Juan D.": for the wide screen anybody walking past can read.</param>
/// <param name="IsFree">Put in for free by the venue.</param>
public sealed record MatchBoardPlayer(Guid ParticipantId, string FullName, string DisplayName, bool IsFree);

/// <summary>
/// One match, its two teams seat by seat. A null seat is open, waiting for the
/// next check-in. A finished match lists who was on court at the end.
/// </summary>
/// <param name="Status">"Forming", "Ready", "Playing" or "Finished".</param>
public sealed record MatchBoardMatch(
    Guid MatchId,
    int Number,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<MatchBoardPlayer?> TeamA,
    IReadOnlyList<MatchBoardPlayer?> TeamB);

/// <summary>
/// One session's matches, for the desk to run and the wide screen to show:
/// the ones played, the one on court, the ones still to come, and who is done
/// for the day.
/// </summary>
public sealed record MatchBoard(
    Guid OpenPlayId,
    string Title,
    string FacilityName,
    string CourtName,
    string UnitLabel,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    string SportKey,
    string SportName,
    int PlayersPerTeam,
    // The session has finished on the venue's clock.
    bool IsOver,
    string? BackgroundPhotoUrl,
    IReadOnlyList<MatchBoardMatch> Matches,
    // Checked in and not on a team yet. Usually nobody: the line fills the
    // matches still to come as soon as somebody arrives.
    IReadOnlyList<MatchBoardPlayer> Waiting,
    IReadOnlyList<MatchBoardPlayer> Done);

/// <summary>
/// Starts the next match. Short of players, it starts only when
/// <paramref name="EvenIfShort"/> says so — the desk was asked first.
/// </summary>
public sealed record StartMatchRequest(bool EvenIfShort);
