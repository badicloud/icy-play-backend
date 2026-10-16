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
/// The desk's photos of a session's results: best players, winning team, the
/// whole group. Owners and attendants, on the venues they work. Kept two days.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
public sealed class DeskResultPhotosController(
    IDeskResultPhotoService photos,
    ICloudinaryAssetService assets) : ControllerBase
{
    /// <summary>
    /// Signs one upload into the results folder, fixed here: a caller that
    /// could name its own could write anywhere on the account.
    /// </summary>
    [HttpPost("api/v1/desk/open-play-result-photos/signature")]
    public IActionResult Signature() =>
        Ok(new ApiEnvelope<CloudinaryUploadSignature>(assets.CreateUploadSignature(OpenPlayPhotos.ResultsFolder)));

    /// <summary>Saves the photo of one kind — <c>BestPlayers</c>, <c>WinningTeam</c> or <c>Group</c> — in place of any before.</summary>
    [HttpPut("api/v1/desk/open-plays/{openPlayId:guid}/sessions/{date}/photos/{kind}")]
    public async Task<IActionResult> Save(Guid openPlayId, DateOnly date, string kind, OpenPlayPhotoInput photo, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        return Answer(await photos.SaveAsync(actor, openPlayId, date, kind, photo, ct));
    }

    [HttpDelete("api/v1/desk/open-plays/{openPlayId:guid}/sessions/{date}/photos/{kind}")]
    public async Task<IActionResult> Remove(Guid openPlayId, DateOnly date, string kind, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        return Answer(await photos.RemoveAsync(actor, openPlayId, date, kind, ct));
    }

    private IActionResult Answer(OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>> result) =>
        result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyList<MatchResultPhoto>>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);

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
