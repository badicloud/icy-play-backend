using System.Security.Claims;
using FluentValidation;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The venue's owner putting people on their own desk, and taking them off:
/// the same as the platform admin does from the console, with the same
/// invitation email.
///
/// Owners only, and only for their own venues. Which owner is never taken from
/// the request: it is the one signed in, and a venue that is not theirs answers
/// the same as one that does not exist.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.FacilityOwner)]
[Route("api/v1/desk/venues/{facilityId:guid}/attendants")]
public sealed class DeskAttendantsController(
    IFacilityAttendantService attendants,
    IValidator<InviteAttendantRequest> inviteValidator) : ControllerBase
{
    /// <summary>Who works this venue's desk, the owner first.</summary>
    [HttpGet]
    public async Task<IActionResult> List(Guid facilityId, CancellationToken ct)
    {
        if (await OwnerIdAsync(ct) is not Guid ownerId)
        {
            return Forbidden();
        }

        var result = await attendants.ListAsync(ownerId, facilityId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<FacilityAttendantDetail>>(result.Value!))
            : AttendantFailureResults.ToResult(result.Failure);
    }

    /// <summary>Whether an address can be put on this desk, asked while the owner types.</summary>
    [HttpGet("check")]
    public async Task<IActionResult> CheckEmail(Guid facilityId, [FromQuery] string email, CancellationToken ct)
    {
        if (await OwnerIdAsync(ct) is not Guid ownerId)
        {
            return Forbidden();
        }

        var result = await attendants.CheckEmailAsync(ownerId, facilityId, email, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<AttendantEmailCheck>(result.Value!))
            : AttendantFailureResults.ToResult(result.Failure);
    }

    /// <summary>Puts somebody on the desk and emails them the link to set their password.</summary>
    [HttpPost]
    public async Task<IActionResult> Invite(Guid facilityId, InviteAttendantRequest request, CancellationToken ct)
    {
        var validation = await inviteValidator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return BadRequest(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.ValidationError,
                "The request is invalid.",
                validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()))));
        }

        if (await OwnerIdAsync(ct) is not Guid ownerId || CurrentActor() is not AuditActor actor)
        {
            return Forbidden();
        }

        var result = await attendants.InviteAsync(ownerId, facilityId, request, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<FacilityAttendantDetail>(result.Value!))
            : AttendantFailureResults.ToResult(result.Failure);
    }

    /// <summary>Sends another activation link. Any link already outstanding stops working.</summary>
    [HttpPost("{attendantId:guid}/resend-invitation")]
    public async Task<IActionResult> ResendInvitation(Guid facilityId, Guid attendantId, CancellationToken ct)
    {
        if (await OwnerIdAsync(ct) is not Guid ownerId || CurrentActor() is not AuditActor actor)
        {
            return Forbidden();
        }

        var result = await attendants.ResendInvitationAsync(ownerId, facilityId, attendantId, actor, ct);

        return result.Succeeded ? NoContent() : AttendantFailureResults.ToResult(result.Failure);
    }

    /// <summary>Takes somebody off the desk. Their name stays on what they confirmed.</summary>
    [HttpDelete("{attendantId:guid}")]
    public async Task<IActionResult> Remove(
        Guid facilityId,
        Guid attendantId,
        [FromQuery] string? reason,
        CancellationToken ct)
    {
        if (await OwnerIdAsync(ct) is not Guid ownerId || CurrentActor() is not AuditActor actor)
        {
            return Forbidden();
        }

        var result = await attendants.RemoveAsync(ownerId, facilityId, attendantId, reason, actor, ct);

        return result.Succeeded ? NoContent() : AttendantFailureResults.ToResult(result.Failure);
    }

    private async Task<Guid?> OwnerIdAsync(CancellationToken ct) =>
        CurrentUserId() is Guid userId ? await attendants.FacilityOwnerIdOfAsync(userId, ct) : null;

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
            UserRoleName.FacilityOwner,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent);
    }

    private ObjectResult Forbidden() =>
        StatusCode(
            StatusCodes.Status403Forbidden,
            new ApiErrorEnvelope(new ApiError(ErrorCodes.Forbidden, "Only a venue's owner can manage its attendants.")));
}
