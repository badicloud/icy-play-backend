using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Audit;

/// <summary>
/// One recorded change. Written in the same transaction as the change itself,
/// so a record can never exist without its entry or an entry without its
/// record.
/// </summary>
public sealed class AuditLog : Entity
{
    private AuditLog()
    {
    }

    public AuditLog(
        Guid? actorUserId,
        string actorRole,
        string action,
        string entityType,
        Guid entityId,
        string? oldValuesJson,
        string? newValuesJson,
        string? reason,
        string? ipAddress,
        string? userAgent,
        DateTimeOffset occurredAt)
    {
        ActorUserId = actorUserId;
        ActorRole = actorRole;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        OldValuesJson = oldValuesJson;
        NewValuesJson = newValuesJson;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        IpAddress = ipAddress;
        UserAgent = userAgent;
        CreatedAt = occurredAt;
    }

    /// <summary>Null when the platform itself did it rather than a person.</summary>
    public Guid? ActorUserId
    {
        get; private set;
    }
    public string ActorRole { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId
    {
        get; private set;
    }

    /// <summary>
    /// Only the fields that actually changed, not the whole entity. A diff of
    /// three lines can be read; a dump of forty cannot.
    /// </summary>
    public string? OldValuesJson
    {
        get; private set;
    }
    public string? NewValuesJson
    {
        get; private set;
    }
    public string? Reason
    {
        get; private set;
    }
    public string? IpAddress
    {
        get; private set;
    }
    public string? UserAgent
    {
        get; private set;
    }
}

public static class AuditAction
{
    public const string FacilityOwnerOnboarded = "FacilityOwnerOnboarded";
    public const string FacilityOwnerBusinessUpdated = "FacilityOwnerBusinessUpdated";
    public const string FacilityOwnerInvitationSent = "FacilityOwnerInvitationSent";
    public const string FacilityUpdated = "FacilityUpdated";
    public const string FacilityAmenitiesUpdated = "FacilityAmenitiesUpdated";
    public const string FacilityHoursUpdated = "FacilityHoursUpdated";
    public const string ContractCommenced = "ContractCommenced";
    public const string ContractCancelled = "ContractCancelled";
    public const string ContractDocumentReplaced = "ContractDocumentReplaced";
}

public static class AuditEntityType
{
    public const string FacilityOwner = "FacilityOwner";
    public const string Facility = "Facility";
    public const string FacilityOwnerContract = "FacilityOwnerContract";
}
