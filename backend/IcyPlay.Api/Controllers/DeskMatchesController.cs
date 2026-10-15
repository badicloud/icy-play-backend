using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The matches of an open play session: the board, and the desk's buttons on
/// it. Owners and attendants, on the venues they work. Every answer is the
/// board as it now stands.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
public sealed class DeskMatchesController(IDeskMatchService matches) : ControllerBase
{
    [HttpGet("api/v1/desk/open-plays/{openPlayId:guid}/sessions/{date}/matches")]
    public async Task<IActionResult> Board(Guid openPlayId, DateOnly date, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        return Answer(await matches.BoardAsync(userId, openPlayId, date, ct));
    }

    /// <summary>Starts the next match. Short of players, only with <c>evenIfShort</c>.</summary>
    [HttpPost("api/v1/desk/open-play-matches/{matchId:guid}/start")]
    public async Task<IActionResult> Start(Guid matchId, StartMatchRequest request, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        return Answer(await matches.StartAsync(actor, matchId, request, ct));
    }

    [HttpPost("api/v1/desk/open-play-matches/{matchId:guid}/finish")]
    public async Task<IActionResult> Finish(Guid matchId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        return Answer(await matches.FinishAsync(actor, matchId, ct));
    }

    /// <summary>A player is done for the day. The line adjusts around them.</summary>
    [HttpPost("api/v1/desk/open-play-participants/{participantId:guid}/done")]
    public async Task<IActionResult> Done(Guid participantId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        return Answer(await matches.DoneAsync(actor, participantId, ct));
    }

    [HttpPost("api/v1/desk/open-play-participants/{participantId:guid}/back-in")]
    public async Task<IActionResult> BackIn(Guid participantId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        return Answer(await matches.BackInAsync(actor, participantId, ct));
    }

    private IActionResult Answer(OpenPlayRegistrationResult<MatchBoard> result) =>
        result.Succeeded
            ? Ok(new ApiEnvelope<MatchBoard>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);

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
