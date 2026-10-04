using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The door of an open play session: the roster, a scan of a player's pass,
/// and checking in or taking it back by hand. Owners and attendants, on the
/// venues they work.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
public sealed class DeskCheckInController(IDeskCheckInService checkIns) : ControllerBase
{
    private const string Session = "api/v1/desk/open-plays/{openPlayId:guid}/sessions/{date}/check-in";

    /// <summary>One session's roster, for the scanner and the wide screen.</summary>
    [HttpGet(Session)]
    public async Task<IActionResult> Roster(Guid openPlayId, DateOnly date, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await checkIns.RosterAsync(userId, openPlayId, date, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CheckInRoster>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    /// <summary>What the camera read. Always answers with an outcome to read out.</summary>
    [HttpPost(Session + "/scan")]
    public async Task<IActionResult> Scan(
        Guid openPlayId,
        DateOnly date,
        CheckInScanRequest request,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await checkIns.ScanAsync(actor, openPlayId, date, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CheckInScanResult>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    /// <summary>Checks a registered player in by hand, with the venue's check-in code.</summary>
    [HttpPost("api/v1/desk/open-play-check-ins/{registrationId:guid}")]
    public async Task<IActionResult> CheckIn(Guid registrationId, CheckInCodeRequest request, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await checkIns.CheckInAsync(actor, registrationId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CheckInRoster>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    /// <summary>
    /// Takes back a check-in made by mistake, with the venue's check-in code.
    /// A POST rather than a DELETE, because it carries the code in its body.
    /// </summary>
    [HttpPost("api/v1/desk/open-play-check-ins/{registrationId:guid}/undo")]
    public async Task<IActionResult> Undo(Guid registrationId, CheckInCodeRequest request, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await checkIns.UndoAsync(actor, registrationId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CheckInRoster>(result.Value!))
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
