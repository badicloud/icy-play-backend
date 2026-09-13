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
