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
    /// <summary>The dials the venue sets from its own desk.</summary>
    public const string FacilityOwnerDeskSettingsUpdated = "FacilityOwnerDeskSettingsUpdated";
    /// <summary>The move limit and notice, set for a venue by the platform admin.</summary>
    public const string FacilityOwnerMoveRulesUpdated = "FacilityOwnerMoveRulesUpdated";
    public const string FacilityOwnerInvitationSent = "FacilityOwnerInvitationSent";
    /// <summary>
    /// Demonstration venues were removed in bulk. Kept even though the rows
    /// themselves are gone: the trail is the only thing left saying it
    /// happened, and it outlives what it describes by design.
    /// </summary>
    public const string SeededDataRemoved = "SeededDataRemoved";
    public const string FacilityUpdated = "FacilityUpdated";
    public const string FacilityAmenitiesUpdated = "FacilityAmenitiesUpdated";
    public const string FacilityPhotosUpdated = "FacilityPhotosUpdated";
    public const string FacilityHoursUpdated = "FacilityHoursUpdated";
    public const string FacilityAttendantAdded = "FacilityAttendantAdded";
    public const string FacilityAttendantRemoved = "FacilityAttendantRemoved";
    public const string FacilityAttendantInvitationSent = "FacilityAttendantInvitationSent";
    /// <summary>
    /// What happens to a booking, in the customer's own words.
    ///
    /// The desk already wrote the two it gives; the rest were happening with
    /// nothing recorded at all — a booking could be made, paid for and moved
    /// to another court, and the only trace was the booking's own state, which
    /// says where it ended up and never how it got there.
    /// </summary>
    public const string BookingCreated = "BookingCreated";
    public const string BookingPaymentSubmitted = "BookingPaymentSubmitted";
    public const string BookingMoved = "BookingMoved";
    public const string BookingUpgradeRequested = "BookingUpgradeRequested";
    public const string BookingUpgradePaymentSubmitted = "BookingUpgradePaymentSubmitted";
    public const string BookingUpgradeApproved = "BookingUpgradeApproved";
    public const string BookingUpgradeDeclined = "BookingUpgradeDeclined";

    /// <summary>
    /// A move with nothing to pay, from being asked for to being answered. The
    /// upgrade has its own three because money changes hands in the middle.
    /// </summary>
    public const string BookingMoveRequested = "BookingMoveRequested";
    public const string BookingMoveApproved = "BookingMoveApproved";
    public const string BookingMoveDeclined = "BookingMoveDeclined";

    /// <summary>
    /// A hold that ran out with nothing paid.
    ///
    /// Never written to the trail, because nothing notices it happening: a
    /// booking does not change when its hold ends, it simply stops holding.
    /// The name exists so a history can still say it, worked out from the
    /// booking the same way the badge on the card is.
    /// </summary>
    public const string BookingHoldExpired = "BookingHoldExpired";
    public const string BookingConfirmed = "BookingConfirmed";
    public const string BookingRejected = "BookingRejected";
    public const string FacilityAttendantMoneyAccessChanged = "FacilityAttendantMoneyAccessChanged";
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
    /// <summary>One line for a whole uploaded file, with what it did to it.</summary>
    public const string HolidaysImported = "HolidaysImported";
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
    public const string Booking = "Booking";
}
