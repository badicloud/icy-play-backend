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
/// The holiday calendar, managed from the console. The fixed Philippine
/// holidays are seeded, but Maundy Thursday, Eid'l Fitr and the proclaimed
/// special days land on a different date every year, so charging a holiday
/// rate correctly has to be a matter of data rather than of a deploy.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/holidays")]
public sealed class AdminHolidaysController(
    IHolidayService holidays,
    IValidator<CreateHolidayRequest> createValidator,
    IValidator<UpdateHolidayRequest> updateValidator) : ControllerBase
{
    /// <summary>Retired holidays are left out unless asked for.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool includeRetired = false,
        CancellationToken ct = default) =>
        Ok(new ApiEnvelope<IReadOnlyCollection<HolidayListItem>>(
            await holidays.ListAsync(includeRetired, ct)));

    [HttpPost]
    public async Task<IActionResult> Create(CreateHolidayRequest request, CancellationToken ct)
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

        var result = await holidays.CreateAsync(request, actor, ct);

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, new ApiEnvelope<Guid>(result.Value))
            : Conflict(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.Conflict,
                "That holiday is already on the calendar for that date.")));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateHolidayRequest request, CancellationToken ct)
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

        var result = await holidays.UpdateAsync(id, request, actor, ct);

        return result.Failure switch
        {
            CourtFailure.None => NoContent(),
            CourtFailure.DuplicateHoliday => Conflict(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.Conflict,
                "That holiday is already on the calendar for that date."))),
            _ => NotFoundHoliday()
        };
    }

    /// <summary>
    /// Retires rather than deletes. A booking priced as a holiday needs the day
    /// that made it one to still be there when the receipt is questioned.
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

        var result = await holidays.SetActiveAsync(id, isActive, actor, ct);
        return result.Succeeded ? NoContent() : NotFoundHoliday();
    }

    private IActionResult NotFoundHoliday() =>
        NotFound(new ApiErrorEnvelope(new ApiError(ErrorCodes.NotFound, "No holiday with that id.")));

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
