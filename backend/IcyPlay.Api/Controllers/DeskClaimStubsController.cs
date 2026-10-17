using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The prizes an open play gives its best players, as claim stubs: the list,
/// and marking one claimed. Owners and attendants, on the venues they work.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
public sealed class DeskClaimStubsController(IDeskClaimStubService stubs) : ControllerBase
{
    [HttpGet("api/v1/desk/open-plays/{openPlayId:guid}/claim-stubs")]
    public async Task<IActionResult> List(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await stubs.ListAsync(actor.UserId!.Value, openPlayId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyList<ClaimStubView>>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    /// <summary>Marks the stub with this code claimed: the player has shown it and had their prize.</summary>
    [HttpPost("api/v1/desk/claim-stubs/claim")]
    public async Task<IActionResult> Claim(ClaimStubRequest request, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await stubs.ClaimAsync(actor, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<ClaimStubView>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    private AuditActor? CurrentActor()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return null;
        }

        var userAgent = Request.Headers.UserAgent.ToString();

        return new AuditActor(
            userId,
            User.IsInRole(UserRoleName.FacilityOwner)
                ? UserRoleName.FacilityOwner
                : UserRoleName.FacilityAttendant,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }
}
