using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The matches of an open play session, run from the desk: the board, and
/// the buttons that move it. Owners and attendants, on the venues they work.
/// Each answer is the board as it stands after, so the page needs no second
/// request.
/// </summary>
public interface IDeskMatchService
{
    Task<OpenPlayRegistrationResult<MatchBoard>> BoardAsync(
        Guid userId,
        Guid openPlayId,
        DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>Starts the next match, the court being free.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> StartAsync(
        AuditActor actor,
        Guid matchId,
        StartMatchRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends the match on court with who won and, if given, the score. Its
    /// players go to the end of the line.
    /// </summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> FinishAsync(
        AuditActor actor,
        Guid matchId,
        MatchResultRequest request,
        CancellationToken cancellationToken);

    /// <summary>Puts a finished match's result right. The line stays as it is.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> CorrectResultAsync(
        AuditActor actor,
        Guid matchId,
        MatchResultRequest request,
        CancellationToken cancellationToken);

    /// <summary>The open play's leaderboard over all its sessions so far.</summary>
    Task<OpenPlayRegistrationResult<SeriesLeaderboard>> SeriesLeaderboardAsync(
        Guid userId,
        Guid openPlayId,
        CancellationToken cancellationToken);

    /// <summary>A player is done for the day: tired, hurt or leaving. The line adjusts around them.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> DoneAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// "Play next match": a player in the next match goes to the end of the
    /// line, and the player the desk picked from a later match takes their seat.
    /// </summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> PlayNextMatchAsync(
        AuditActor actor,
        Guid participantId,
        PlayNextMatchRequest request,
        CancellationToken cancellationToken);

    /// <summary>Moves a player in the next match to the other team, to even the sides out.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> MoveToTeamAsync(
        AuditActor actor,
        Guid participantId,
        MoveToTeamRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends the session: no more games, and the results stand. The matches not
    /// started go. Not while a match is on court.
    /// </summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> EndSessionAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>Opens an ended session again, while its time is not up.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> ReopenSessionAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>A player done for the day comes back, at the end of the line.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> BackInAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken cancellationToken);
}
