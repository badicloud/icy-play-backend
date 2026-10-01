using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// A player's open play registrations: join a date, send the GCash receipt, and
/// read where it stands. Signed-in customers only, and only their own.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.Customer)]
[Route("api/v1/open-play-registrations")]
public sealed class OpenPlayRegistrationsController(ICustomerOpenPlayService registrations) : ControllerBase
{
    /// <summary>
    /// Registers for one date and holds the spot for the venue's payment hold.
    /// Pressing it again while holding a spot answers with the same registration.
    /// </summary>
    [HttpPost("~/api/v1/open-plays/{openPlayId:guid}/registrations")]
    public async Task<IActionResult> Register(
        Guid openPlayId,
        RegisterForOpenPlayRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await registrations.RegisterAsync(userId, openPlayId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<OpenPlayRegistrationDetail>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        return Ok(new ApiEnvelope<IReadOnlyCollection<OpenPlayRegistrationDetail>>(
            await registrations.ListMineAsync(userId, ct)));
    }

    [HttpGet("{registrationId:guid}")]
    public async Task<IActionResult> Get(Guid registrationId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await registrations.GetAsync(userId, registrationId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<OpenPlayRegistrationDetail>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    /// <summary>
    /// Records the receipt the browser has just put in Cloudinary. Sending it
    /// hands the registration to the venue's desk.
    /// </summary>
    [HttpPost("{registrationId:guid}/receipt")]
    public async Task<IActionResult> Receipt(
        Guid registrationId,
        OpenPlayReceiptRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await registrations.SendReceiptAsync(userId, registrationId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<OpenPlayRegistrationDetail>(result.Value!))
            : OpenPlayRegistrationErrors.Failure(result);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
