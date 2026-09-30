using IcyPlay.Application.Audit;
using IcyPlay.Application.Common;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// Changes to an owner already on the platform. Separate from onboarding
/// because the two have opposite shapes: onboarding writes everything once,
/// editing writes one section at a time and has to say what it changed.
/// </summary>
public interface IFacilityOwnerEditService
{
    /// <summary>
    /// Sets where the venue is paid and how long it holds a court unpaid. The
    /// QR code is already in Cloudinary by the time this is called; only its
    /// link comes through here, and it is checked.
    /// </summary>
    Task<EditResult> UpdatePaymentDetailsAsync(
        Guid facilityOwnerId,
        UpdatePaymentDetailsRequest request,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// Sets how many days ahead the venue's courts can be booked, how many
    /// times one of its bookings may move, and how many days before it starts
    /// moves close.
    /// </summary>
    Task<EditResult> UpdateBookingRulesAsync(
        Guid facilityOwnerId,
        UpdateBookingRulesRequest request,
        AuditActor actor,
        CancellationToken ct);

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
    /// Corrects the dates and notes of a term. Refused on a cancelled one:
    /// cancelling is what ends a term, and moving the dates of an ended one
    /// says nothing about what was agreed.
    /// </summary>
    Task<EditResult> UpdateContractTermAsync(
        Guid facilityOwnerId,
        Guid contractId,
        UpdateContractTermRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets what IcyPlay charges under one term. Its own call, because a rate
    /// is renegotiated far more often than a term is renewed.
    /// </summary>
    Task<EditResult> UpdateContractRatesAsync(
        Guid facilityOwnerId,
        Guid contractId,
        UpdateContractRatesRequest request,
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

    /// <summary>
    /// Everything recorded against this owner, newest first, one page at a
    /// time. An owner's trail only grows, and reading all of it to show the
    /// first screenful got slower with every edit.
    /// </summary>
    Task<PagedResult<ActivityEntry>> ListActivityAsync(
        Guid facilityOwnerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

/// <summary>How much of an owner's trail one request reads.</summary>
public static class ActivityPaging
{
    /// <summary>A screenful.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>The most one request may ask for, whatever it asks.</summary>
    public const int LargestPage = 50;
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
