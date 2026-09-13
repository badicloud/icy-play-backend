using IcyPlay.Application.Audit;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// The sports lookup, managed rather than seeded once. A venue that hosts
/// something nobody thought of should not need a deploy.
/// </summary>
public interface ISportService
{
    Task<IReadOnlyCollection<SportListItem>> ListAsync(
        bool includeRetired,
        CancellationToken cancellationToken);

    Task<CourtResult<Guid>> CreateAsync(
        CreateSportRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    Task<CourtResult<bool>> UpdateAsync(
        Guid id,
        UpdateSportRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retires rather than deletes: courts reference the row, and removing it
    /// would take their record of what they host with it.
    /// </summary>
    Task<CourtResult<bool>> SetActiveAsync(
        Guid id,
        bool isActive,
        AuditActor actor,
        CancellationToken cancellationToken);
}
