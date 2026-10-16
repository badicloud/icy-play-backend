namespace IcyPlay.Application.OpenPlays;

/// <summary>One player on the matches board.</summary>
/// <param name="ParticipantId">Their place in the session's line: what Done and Back in take.</param>
/// <param name="DisplayName">"Juan D.": for the wide screen anybody walking past can read.</param>
/// <param name="IsFree">Put in for free by the venue.</param>
/// <param name="Played">
/// Finished matches they were on court in this session, even for part of one:
/// so the desk can see at a glance who has had a game and who has not.
/// </param>
public sealed record MatchBoardPlayer(Guid ParticipantId, string FullName, string DisplayName, bool IsFree, int Played = 0);

/// <summary>
/// One match, its two teams seat by seat. A null seat is open, waiting for the
/// next check-in. A finished match lists who was on court at the end.
/// </summary>
/// <param name="Status">"Forming", "Ready", "Playing" or "Finished".</param>
/// <param name="Result">"A", "B" or "Draw" once finished; null before, or for a match finished before results were kept.</param>
/// <param name="ScoreA">The score, when the desk gave one: both or neither.</param>
public sealed record MatchBoardMatch(
    Guid MatchId,
    int Number,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<MatchBoardPlayer?> TeamA,
    IReadOnlyList<MatchBoardPlayer?> TeamB,
    string? Result,
    int? ScoreA,
    int? ScoreB);

/// <summary>One player's record in the session, for the leaderboard.</summary>
/// <param name="Rank">By wins alone: the same wins, the same place.</param>
/// <param name="IsBest">Level on the most wins: one of the session's best players.</param>
public sealed record MatchStanding(
    MatchBoardPlayer Player,
    int Rank,
    int Played,
    int Won,
    int Drawn,
    int Lost,
    bool IsBest);

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
    IReadOnlyList<MatchBoardPlayer> Done,
    // Most wins first. Only matches with a result count.
    IReadOnlyList<MatchStanding> Standings);

/// <summary>
/// How a match ended: <paramref name="Result"/> is "A", "B" or "Draw". The
/// score is optional, for both teams or neither, and has to agree with it.
/// </summary>
public sealed record MatchResultRequest(string Result, int? ScoreA, int? ScoreB);

/// <summary>
/// One player over every session of an open play. Players who registered
/// only: a free player is somebody new each session, so they count in their
/// session's leaderboard and not here.
/// </summary>
/// <param name="Sessions">Sessions they played a match with a result in.</param>
public sealed record SeriesStanding(
    string FullName,
    string DisplayName,
    int Rank,
    int Sessions,
    int Played,
    int Won,
    int Drawn,
    int Lost);

/// <summary>The open play's leaderboard over all its sessions so far, most wins first.</summary>
public sealed record SeriesLeaderboard(
    Guid OpenPlayId,
    string Title,
    string SportName,
    // Sessions with at least one match with a result.
    int Sessions,
    IReadOnlyList<SeriesStanding> Standings);

/// <summary>
/// Starts the next match. Short of players, it starts only when
/// <paramref name="EvenIfShort"/> says so — the desk was asked first.
/// </summary>
public sealed record StartMatchRequest(bool EvenIfShort);

/// <summary>
/// "Play next match": who takes the seat — a player in a later match, picked
/// by the desk.
/// </summary>
public sealed record PlayNextMatchRequest(Guid ReplacementParticipantId);

/// <summary>Move to team: "A" or "B", for a player in the next match.</summary>
public sealed record MoveToTeamRequest(string Team);
