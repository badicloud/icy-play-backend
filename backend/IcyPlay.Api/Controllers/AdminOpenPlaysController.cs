using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// One facility owner's open plays, for the platform admin: every action the
/// desk has, under the same rules. The owner in the address is the scope, so an
/// open play at another owner's venue answers as not found.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/facility-owners/{facilityOwnerId:guid}/open-plays")]
public sealed class AdminOpenPlaysController(
    IAdminOpenPlayService openPlays,
    ICloudinaryAssetService assets) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid facilityOwnerId, CancellationToken ct)
    {
        var result = await openPlays.ListForOwnerAsync(facilityOwnerId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<DeskOpenPlay>>(result.Value!))
            : OpenPlayErrors.Failure(result);
    }

    /// <summary>The owner's venues and courts, each venue with its own today: what the form picks from.</summary>
    [HttpGet("places")]
    public async Task<IActionResult> Places(Guid facilityOwnerId, CancellationToken ct) =>
        Ok(new ApiEnvelope<OpenPlayPlaces>(await openPlays.PlacesForOwnerAsync(facilityOwnerId, ct)));

    [HttpGet("{openPlayId:guid}")]
    public async Task<IActionResult> Get(Guid facilityOwnerId, Guid openPlayId, CancellationToken ct)
    {
        var result = await openPlays.GetForOwnerAsync(facilityOwnerId, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid facilityOwnerId, OpenPlayInput input, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.CreateForOwnerAsync(facilityOwnerId, actor, input, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<OpenPlaySaved>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpPut("{openPlayId:guid}")]
    public async Task<IActionResult> Update(
        Guid facilityOwnerId,
        Guid openPlayId,
        OpenPlayInput input,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.UpdateForOwnerAsync(facilityOwnerId, actor, openPlayId, input, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<OpenPlaySaved>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpPost("{openPlayId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid facilityOwnerId, Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.PublishForOwnerAsync(facilityOwnerId, actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpPost("{openPlayId:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid facilityOwnerId, Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.UnpublishForOwnerAsync(facilityOwnerId, actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpPost("{openPlayId:guid}/end")]
    public async Task<IActionResult> End(Guid facilityOwnerId, Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.EndForOwnerAsync(facilityOwnerId, actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    /// <summary>Moves when check-in opens. Allowed after publishing.</summary>
    [HttpPut("{openPlayId:guid}/check-in-window")]
    public async Task<IActionResult> CheckInWindow(
        Guid facilityOwnerId,
        Guid openPlayId,
        OpenPlayCheckInWindowInput input,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.SetCheckInWindowForOwnerAsync(facilityOwnerId, actor, openPlayId, input, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpDelete("{openPlayId:guid}")]
    public async Task<IActionResult> Delete(Guid facilityOwnerId, Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.DeleteDraftForOwnerAsync(facilityOwnerId, actor, openPlayId, ct);

        return result.Succeeded ? NoContent() : OpenPlayErrors.Failure(result);
    }

    /// <summary>Signs a browser upload of a cover photo into the open play folder, and nowhere else.</summary>
    [HttpPost("photo-signature")]
    public IActionResult PhotoSignature(Guid facilityOwnerId) =>
        Ok(new ApiEnvelope<CloudinaryUploadSignature>(assets.CreateUploadSignature(OpenPlayPhotos.Folder)));

    [HttpPut("{openPlayId:guid}/photo")]
    public async Task<IActionResult> SetPhoto(
        Guid facilityOwnerId,
        Guid openPlayId,
        OpenPlayPhotoInput photo,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.SetCoverPhotoForOwnerAsync(facilityOwnerId, actor, openPlayId, photo, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
    }

    [HttpDelete("{openPlayId:guid}/photo")]
    public async Task<IActionResult> RemovePhoto(Guid facilityOwnerId, Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.RemoveCoverPhotoForOwnerAsync(facilityOwnerId, actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : OpenPlayErrors.Failure(result);
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
            UserRoleName.PlatformAdmin,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }
}
