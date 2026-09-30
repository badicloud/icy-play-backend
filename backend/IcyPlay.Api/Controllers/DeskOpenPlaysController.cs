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
/// The desk's open plays. Owners and attendants both, on the venues they work.
///
/// Saved as a draft first, published once it is final. A draft is invisible to
/// customers and holds nothing. A published one is open for registration,
/// holds the court, and cannot be changed.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
[Route("api/v1/desk/open-plays")]
public sealed class DeskOpenPlaysController(
    IDeskOpenPlayService openPlays,
    ICloudinaryAssetService assets) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? facilityId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await openPlays.ListAsync(userId, facilityId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<DeskOpenPlay>>(result.Value!))
            : Failure(result);
    }

    [HttpGet("{openPlayId:guid}")]
    public async Task<IActionResult> Get(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await openPlays.GetAsync(userId, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : Failure(result);
    }

    /// <summary>Saves a new draft. Anything it would clash with comes back as a warning.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(OpenPlayInput input, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.CreateAsync(actor, input, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<OpenPlaySaved>(result.Value!)) : Failure(result);
    }

    /// <summary>Changes a draft. A published open play cannot be changed.</summary>
    [HttpPut("{openPlayId:guid}")]
    public async Task<IActionResult> Update(Guid openPlayId, OpenPlayInput input, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.UpdateAsync(actor, openPlayId, input, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<OpenPlaySaved>(result.Value!)) : Failure(result);
    }

    [HttpPost("{openPlayId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.PublishAsync(actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : Failure(result);
    }

    [HttpPost("{openPlayId:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.UnpublishAsync(actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : Failure(result);
    }

    [HttpPost("{openPlayId:guid}/end")]
    public async Task<IActionResult> End(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.EndAsync(actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : Failure(result);
    }

    /// <summary>
    /// Signs a browser upload of a cover photo straight into Cloudinary. One
    /// folder, fixed here: a caller that could name its own could write anywhere
    /// in the account.
    /// </summary>
    [HttpPost("photo-signature")]
    public IActionResult PhotoSignature() =>
        CurrentUserId() is null
            ? Unauthorized()
            : Ok(new ApiEnvelope<CloudinaryUploadSignature>(assets.CreateUploadSignature(OpenPlayPhotos.Folder)));

    /// <summary>Sets or replaces the cover photo. Allowed after publishing: the photo is outside the lock.</summary>
    [HttpPut("{openPlayId:guid}/photo")]
    public async Task<IActionResult> SetPhoto(Guid openPlayId, OpenPlayPhotoInput photo, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.SetCoverPhotoAsync(actor, openPlayId, photo, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : Failure(result);
    }

    [HttpDelete("{openPlayId:guid}/photo")]
    public async Task<IActionResult> RemovePhoto(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.RemoveCoverPhotoAsync(actor, openPlayId, ct);

        return result.Succeeded ? Ok(new ApiEnvelope<DeskOpenPlay>(result.Value!)) : Failure(result);
    }

    /// <summary>Deletes a draft that was never published.</summary>
    [HttpDelete("{openPlayId:guid}")]
    public async Task<IActionResult> Delete(Guid openPlayId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await openPlays.DeleteDraftAsync(actor, openPlayId, ct);

        return result.Succeeded ? NoContent() : Failure(result);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    /// <summary>The role recorded is the one that let them in, as on the rest of the desk.</summary>
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

    private static IActionResult Failure<T>(OpenPlayResult<T> result) => OpenPlayErrors.Failure(result);
}
