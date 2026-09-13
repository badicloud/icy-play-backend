using IcyPlay.Application.Audit;
using IcyPlay.Application.Common;

namespace IcyPlay.Application.Facilities;

public interface ICourtService
{
    /// <summary>
    /// Creates the court, and the facility with it when the wizard is adding
    /// one. All or nothing.
    /// </summary>
    Task<CourtResult<CreatedCourtResponse>> CreateAsync(
        CreateCourtRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every facility on the platform, whoever owns it. The console reads this
    /// to answer what is live and what is not.
    /// </summary>
    Task<PagedResult<FacilityInventoryItem>> ListFacilitiesAsync(
        FacilityInventoryQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// One court, read the same way the list reads it, so the detail page and
    /// the list can never disagree about what closes a court.
    /// </summary>
    Task<CourtListItem?> GetAsync(Guid courtId, CancellationToken cancellationToken);

    /// <summary>Corrects a court that already exists. All or nothing.</summary>
    Task<CourtResult<bool>> UpdateAsync(
        Guid courtId,
        UpdateCourtRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets how many playable courts each sport makes here. Sports left out of
    /// the request keep what they had.
    /// </summary>
    Task<CourtResult<bool>> UpdateDivisionsAsync(
        Guid courtId,
        UpdateCourtDivisionsRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets what each sport costs on one court. Sports left out of the request
    /// keep whatever they already had.
    /// </summary>
    Task<CourtResult<bool>> UpdatePricingAsync(
        Guid courtId,
        UpdateCourtPricingRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every court on the platform, narrowed by owner, facility or a search.
    /// Paged, because this grows with the platform rather than with a venue.
    /// </summary>
    Task<PagedResult<CourtInventoryItem>> ListInventoryAsync(
        CourtInventoryQuery query,
        CancellationToken cancellationToken);

    /// <summary>Every court in a facility, in display order, with what closes it.</summary>
    Task<IReadOnlyCollection<CourtListItem>> ListAsync(
        Guid facilityId,
        CancellationToken cancellationToken);

    /// <summary>Closes a whole facility, which closes every court inside it.</summary>
    Task<CourtResult<Guid>> SetFacilityMaintenanceAsync(
        Guid facilityId,
        SetMaintenanceRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    Task<CourtResult<Guid>> SetCourtMaintenanceAsync(
        Guid courtId,
        SetMaintenanceRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>Ends a closure early, leaving the record of it in place.</summary>
    Task<CourtResult<bool>> LiftMaintenanceAsync(
        Guid periodId,
        AuditActor actor,
        CancellationToken cancellationToken);
}
