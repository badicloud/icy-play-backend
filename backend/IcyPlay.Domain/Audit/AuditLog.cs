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
    public const string FacilityOwnerPaymentDetailsUpdated = "FacilityOwnerPaymentDetailsUpdated";
    public const string FacilityOwnerInvitationSent = "FacilityOwnerInvitationSent";
    public const string FacilityUpdated = "FacilityUpdated";
    public const string FacilityAmenitiesUpdated = "FacilityAmenitiesUpdated";
    public const string FacilityPhotosUpdated = "FacilityPhotosUpdated";
    public const string FacilityHoursUpdated = "FacilityHoursUpdated";
    public const string FacilityAttendantAdded = "FacilityAttendantAdded";
    public const string FacilityAttendantRemoved = "FacilityAttendantRemoved";
    public const string FacilityAttendantInvitationSent = "FacilityAttendantInvitationSent";
    public const string ContractCommenced = "ContractCommenced";
    public const string ContractCancelled = "ContractCancelled";
    public const string ContractDocumentReplaced = "ContractDocumentReplaced";
    public const string ContractRatesUpdated = "ContractRatesUpdated";
    public const string ContractTermUpdated = "ContractTermUpdated";
    public const string FacilityCreated = "FacilityCreated";
    public const string CourtCreated = "CourtCreated";
    public const string CourtUpdated = "CourtUpdated";
    public const string CourtSportsUpdated = "CourtSportsUpdated";
    public const string CourtHoursUpdated = "CourtHoursUpdated";
    public const string CourtPhotosUpdated = "CourtPhotosUpdated";
    public const string CourtPricingUpdated = "CourtPricingUpdated";
    public const string CourtDivisionsUpdated = "CourtDivisionsUpdated";
    public const string MaintenanceSet = "MaintenanceSet";
    public const string MaintenanceLifted = "MaintenanceLifted";
    public const string HolidayCreated = "HolidayCreated";
    public const string HolidayUpdated = "HolidayUpdated";
    public const string HolidayRetired = "HolidayRetired";
    public const string HolidayReinstated = "HolidayReinstated";
    public const string SportCreated = "SportCreated";
    public const string SportUpdated = "SportUpdated";
    public const string SportRetired = "SportRetired";
    public const string SportReinstated = "SportReinstated";
}

public static class AuditEntityType
{
    public const string FacilityOwner = "FacilityOwner";
    public const string Facility = "Facility";
    public const string FacilityOwnerContract = "FacilityOwnerContract";
    public const string Court = "Court";
    public const string Sport = "Sport";
    public const string Holiday = "Holiday";
}
