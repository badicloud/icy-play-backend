using IcyPlay.Api.Common;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The amenity checklist the onboarding wizard renders. A lookup rather than an
/// enum, so the list can grow without a deploy.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/amenities")]
public sealed class AdminAmenitiesController(IFacilityOwnerOnboardingService onboarding) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(new ApiEnvelope<IReadOnlyCollection<AmenityListItem>>(
            await onboarding.ListAmenitiesAsync(ct)));
}
