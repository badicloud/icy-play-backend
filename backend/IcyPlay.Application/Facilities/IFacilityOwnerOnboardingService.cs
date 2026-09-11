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

    /// <summary>
    /// One facility owner and everything hanging off them. Null when no such
    /// owner exists, so the API answers 404 rather than an empty shell.
    /// </summary>
    Task<FacilityOwnerDetail?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<PagedResult<FacilityOwnerListItem>?> ListAsync(
        FacilityOwnerQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<AmenityListItem>> ListAmenitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Issues a fresh invitation, retiring any link already outstanding. False
    /// when no such owner exists.
    /// </summary>
    Task<bool> ResendInvitationAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record FacilityOwnerQuery(
    string? Search = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null);
