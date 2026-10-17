using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The prizes an open play gives its best players, as claim stubs: the list
/// for the desk, and marking one claimed when the player shows it. Owners and
/// attendants, on the venues they work.
/// </summary>
public interface IDeskClaimStubService
{
    /// <summary>Every stub of the open play: still to give, given, claimed or expired.</summary>
    Task<OpenPlayRegistrationResult<IReadOnlyList<ClaimStubView>>> ListAsync(
        Guid userId,
        Guid openPlayId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marks the stub with this code claimed: the player has shown it and had
    /// their prize. Only a stub given to a player, not claimed, not expired.
    /// </summary>
    Task<OpenPlayRegistrationResult<ClaimStubView>> ClaimAsync(
        AuditActor actor,
        ClaimStubRequest request,
        CancellationToken cancellationToken);
}
