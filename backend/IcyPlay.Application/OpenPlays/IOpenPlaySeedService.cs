using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// Puts sample open plays on the demonstration venues, and takes them away again.
///
/// Demonstration venues only, never a real one. An open play blocks its court
/// hours, so a sample one on a real venue would stop real customers from booking.
/// </summary>
public interface IOpenPlaySeedService
{
    /// <summary>
    /// A set of sample open plays on every seeded venue: different sports,
    /// levels, weekdays, and early-bird settings. A court whose hours already
    /// have bookings on them is skipped rather than double-sold, and the
    /// result says which ones were skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException">When there is no seeded venue to put them on.</exception>
    Task<OpenPlaySeedResult> BuildAsync(AuditActor actor, CancellationToken cancellationToken);

    /// <summary>The sample open plays standing now, read before removing them.</summary>
    Task<IReadOnlyCollection<SeededOpenPlaySummary>> SeededAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes every sample open play with its sessions and registrations.
    /// Goes by the seeder's marker, never by the title.
    /// </summary>
    Task<OpenPlaySeedRemovalResult> RemoveSeededAsync(AuditActor actor, CancellationToken cancellationToken);
}
