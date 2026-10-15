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

    /// <summary>Ends the match on court. Its players go to the end of the line.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> FinishAsync(
        AuditActor actor,
        Guid matchId,
        CancellationToken cancellationToken);

    /// <summary>A player is done for the day: tired, hurt or leaving. The line adjusts around them.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> DoneAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>A player done for the day comes back, at the end of the line.</summary>
    Task<OpenPlayRegistrationResult<MatchBoard>> BackInAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken cancellationToken);
}
