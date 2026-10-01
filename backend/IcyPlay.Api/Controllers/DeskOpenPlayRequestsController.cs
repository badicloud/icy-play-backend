using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The desk's open play payments: the receipts waiting to be checked, and the
/// players already confirmed. Owners and attendants, on the venues they work.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
[Route("api/v1/desk/open-play-requests")]
public sealed class DeskOpenPlayRequestsController(IDeskOpenPlayRequestService requests) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string tab = OpenPlayRequestTab.Waiting,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await requests.ListAsync(userId, tab, facilityId, page, pageSize, ct);

        if (!result.Succeeded)
        {
            return OpenPlayRegistrationErrors.Failure(result);
        }

        var paged = result.Value!;

        return Ok(new ApiListEnvelope<DeskOpenPlayRequest>(
            paged.Items,
            new PaginationMeta(paged.Page, paged.PageSize, paged.TotalItems, paged.TotalPages)));
    }

    /// <summary>The payment is good: the player is registered and told so.</summary>
    [HttpPost("{registrationId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid registrationId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await requests.ConfirmAsync(actor, registrationId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskOpenPlayRequest>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    /// <summary>The payment is not good: the spot is released and the player told why.</summary>
    [HttpPost("{registrationId:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid registrationId,
        RejectOpenPlayRequest request,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await requests.RejectAsync(actor, registrationId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskOpenPlayRequest>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private AuditActor? CurrentActor()
    {
        if (CurrentUserId() is not Guid userId)
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
