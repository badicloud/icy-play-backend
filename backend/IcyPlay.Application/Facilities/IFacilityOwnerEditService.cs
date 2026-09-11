using IcyPlay.Application.Audit;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// Changes to an owner already on the platform. Separate from onboarding
/// because the two have opposite shapes: onboarding writes everything once,
/// editing writes one section at a time and has to say what it changed.
/// </summary>
public interface IFacilityOwnerEditService
{
    Task<EditResult> UpdateBusinessAsync(
        Guid facilityOwnerId,
        UpdateBusinessRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    Task<EditResult> UpdateFacilityAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        UpdateFacilityRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    Task<EditResult> UpdateOperatingHoursAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        UpdateOperatingHoursRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Adds a term rather than editing one. A signed contract is a record, and
    /// last year's term has to stay readable beside this year's.
    /// </summary>
    Task<EditResult> RenewContractAsync(
        Guid facilityOwnerId,
        RenewContractRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Swaps the signed agreement on a term that already exists. Allowed
    /// because an unreadable or wrong scan is a real mistake, and the swap is
    /// recorded.
    /// </summary>
    Task<EditResult> ReplaceContractDocumentAsync(
        Guid facilityOwnerId,
        Guid contractId,
        ReplaceContractDocumentRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    Task<EditResult> CancelContractAsync(
        Guid facilityOwnerId,
        Guid contractId,
        CancelContractRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>Everything recorded against this owner, newest first.</summary>
    Task<IReadOnlyCollection<ActivityEntry>> ListActivityAsync(
        Guid facilityOwnerId,
        CancellationToken cancellationToken);
}

public sealed record ActivityEntry(
    Guid Id,
    string Action,
    string EntityType,
    Guid EntityId,
    Guid? ActorUserId,
    string? ActorName,
    string ActorRole,
    string? OldValuesJson,
    string? NewValuesJson,
    string? Reason,
    DateTimeOffset CreatedAt);
