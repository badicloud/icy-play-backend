using IcyPlay.Application.Common;

namespace IcyPlay.Application.Facilities;

public interface IFacilityOwnerOnboardingService
{
    /// <summary>
    /// Creates the owner account, the business profile, its documents, the first
    /// facility with its hours and amenities, and the contract that makes the
    /// whole thing bookable. All or nothing.
    /// </summary>
    Task<OnboardingResult<OnboardedFacilityOwnerResponse>> OnboardAsync(
        OnboardFacilityOwnerRequest request,
        Guid onboardedByUserId,
        CancellationToken cancellationToken);

    Task<PagedResult<FacilityOwnerListItem>?> ListAsync(
        FacilityOwnerQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AmenityListItem>> ListAmenitiesAsync(CancellationToken cancellationToken);
}

public sealed record FacilityOwnerQuery(
    string? Search = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null);
