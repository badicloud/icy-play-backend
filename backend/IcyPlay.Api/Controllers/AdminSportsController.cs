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
/// The sports lookup, managed from the console. Seeded with the common ones,
/// but a venue that hosts something nobody thought of should not need a deploy.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/sports")]
public sealed class AdminSportsController(
    ISportService sports,
    IValidator<CreateSportRequest> createValidator,
    IValidator<UpdateSportRequest> updateValidator) : ControllerBase
{
    /// <summary>Retired sports are left out unless asked for, so the wizard only offers live ones.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool includeRetired = false,
        CancellationToken ct = default) =>
        Ok(new ApiEnvelope<IReadOnlyCollection<SportListItem>>(
            await sports.ListAsync(includeRetired, ct)));

    [HttpPost]
    public async Task<IActionResult> Create(CreateSportRequest request, CancellationToken ct)
    {
        var validation = await createValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await sports.CreateAsync(request, actor, ct);

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, new ApiEnvelope<Guid>(result.Value))
            : Conflict(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.Conflict,
                "A sport with that name already exists.")));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateSportRequest request, CancellationToken ct)
    {
        var validation = await updateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await sports.UpdateAsync(id, request, actor, ct);
        return result.Succeeded ? NoContent() : NotFoundSport();
    }

    /// <summary>
    /// Retires rather than deletes. Courts reference the row, and removing it
    /// would take their record of what they host with it.
    /// </summary>
    [HttpPost("{id:guid}/retire")]
    public Task<IActionResult> Retire(Guid id, CancellationToken ct) => SetActiveAsync(id, false, ct);

    [HttpPost("{id:guid}/reinstate")]
    public Task<IActionResult> Reinstate(Guid id, CancellationToken ct) => SetActiveAsync(id, true, ct);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await sports.SetActiveAsync(id, isActive, actor, ct);
        return result.Succeeded ? NoContent() : NotFoundSport();
    }

    private IActionResult NotFoundSport() =>
        NotFound(new ApiErrorEnvelope(new ApiError(ErrorCodes.NotFound, "No sport with that id.")));

    private IActionResult ValidationFailure(FluentValidation.Results.ValidationResult validation) =>
        BadRequest(new ApiErrorEnvelope(new ApiError(
            ErrorCodes.ValidationError,
            "The request is invalid.",
            validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()))));

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
