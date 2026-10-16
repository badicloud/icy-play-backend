using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The photos the desk takes once a session is over: its best players, its
/// winning team, or the whole group. Each is kept for two days. Owners and
/// attendants, on the venues they work. Each answer is the session's photos
/// as they now stand.
/// </summary>
public interface IDeskResultPhotoService
{
    /// <summary>
    /// Saves a photo the browser has put into Cloudinary, in place of any of
    /// the same kind. Only once the session is over.
    /// </summary>
    /// <param name="kind">"BestPlayers", "WinningTeam" or "Group".</param>
    Task<OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>> SaveAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        string kind,
        OpenPlayPhotoInput photo,
        CancellationToken cancellationToken);

    /// <summary>Takes a photo away. It is deleted from storage at the next clean-up.</summary>
    Task<OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>> RemoveAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        string kind,
        CancellationToken cancellationToken);
}
