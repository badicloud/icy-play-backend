using System.Security.Claims;
using FluentValidation;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin")]
public sealed class AdminCourtsController(
    ICourtService courts,
    IValidator<CreateCourtRequest> createValidator,
    IValidator<SetMaintenanceRequest> maintenanceValidator,
    ILogger<AdminCourtsController> logger) : ControllerBase
{
    /// <summary>
    /// The whole court wizard in one call. The facility is either one the owner
    /// already has or is created here alongside the court, and either way it is
    /// a single transaction.
    /// </summary>
    [HttpPost("courts")]
    public async Task<IActionResult> Create(CreateCourtRequest request, CancellationToken ct)
    {
        if (await Invalid(createValidator, request, ct) is IActionResult invalid)
        {
            return invalid;
        }

        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await courts.CreateAsync(request, actor, ct);

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, new ApiEnvelope<CreatedCourtResponse>(result.Value))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The facility inventory: every venue on the platform, whoever owns it.
    /// </summary>
    [HttpGet("facilities")]
    public async Task<IActionResult> ListFacilities(
        [FromQuery] string? search,
        [FromQuery] Guid? facilityOwnerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await courts.ListFacilitiesAsync(
            new FacilityInventoryQuery(search, facilityOwnerId, page, pageSize),
            ct);

        return Ok(new ApiListEnvelope<FacilityInventoryItem>(
            result.Items,
            new PaginationMeta(result.Page, result.PageSize, result.TotalItems, result.TotalPages)));
    }

    [HttpGet("facilities/{facilityId:guid}/courts")]
    public async Task<IActionResult> List(Guid facilityId, CancellationToken ct) =>
        Ok(new ApiEnvelope<IReadOnlyCollection<CourtListItem>>(
            await courts.ListAsync(facilityId, ct)));

    /// <summary>
    /// Closes a whole facility. Every court inside it reads as closed for the
    /// duration, and none of them can lift it.
    /// </summary>
    [HttpPost("facilities/{facilityId:guid}/maintenance")]
    public Task<IActionResult> SetFacilityMaintenance(
        Guid facilityId,
        SetMaintenanceRequest request,
        CancellationToken ct) =>
        SetMaintenanceAsync(request, actor => courts.SetFacilityMaintenanceAsync(facilityId, request, actor, ct), ct);

    [HttpPost("courts/{courtId:guid}/maintenance")]
    public Task<IActionResult> SetCourtMaintenance(
        Guid courtId,
        SetMaintenanceRequest request,
        CancellationToken ct) =>
        SetMaintenanceAsync(request, actor => courts.SetCourtMaintenanceAsync(courtId, request, actor, ct), ct);

    [HttpPost("maintenance/{periodId:guid}/lift")]
    public async Task<IActionResult> LiftMaintenance(Guid periodId, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await courts.LiftMaintenanceAsync(periodId, actor, ct);
        return result.Succeeded ? NoContent() : Failure(result.Failure);
    }

    private async Task<IActionResult> SetMaintenanceAsync(
        SetMaintenanceRequest request,
        Func<AuditActor, Task<CourtResult<Guid>>> set,
        CancellationToken ct)
    {
        if (await Invalid(maintenanceValidator, request, ct) is IActionResult invalid)
        {
            return invalid;
        }

        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await set(actor);

        return result.Succeeded
            ? StatusCode(StatusCodes.Status201Created, new ApiEnvelope<Guid>(result.Value))
            : Failure(result.Failure);
    }

    private async Task<IActionResult?> Invalid<T>(
        IValidator<T> validator,
        T request,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (validation.IsValid)
        {
            return null;
        }

        logger.LogWarning(
            "Court request rejected for fields: {ValidationFields}",
            string.Join(", ", validation.Errors.Select(error => error.PropertyName).Distinct()));

        return BadRequest(new ApiErrorEnvelope(new ApiError(
            ErrorCodes.ValidationError,
            "The request is invalid.",
            validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()))));
    }

    private IActionResult Failure(CourtFailure failure)
    {
        var (status, code, message) = failure switch
        {
            CourtFailure.FacilityOwnerNotFound => (
                StatusCodes.Status404NotFound, ErrorCodes.NotFound, "No facility owner with that id."),
            CourtFailure.FacilityNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That facility does not exist, or does not belong to this owner."),
            CourtFailure.CourtNotFound => (
                StatusCodes.Status404NotFound, ErrorCodes.NotFound, "No court with that id."),
            CourtFailure.MaintenanceNotFound => (
                StatusCodes.Status404NotFound, ErrorCodes.NotFound, "No maintenance period with that id."),
            CourtFailure.UnknownSport => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "One of the selected sports no longer exists."),
            CourtFailure.PrimarySportNotSelected => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The main sport has to be one of the sports selected."),
            CourtFailure.UnknownAmenity => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "One of the selected amenities no longer exists."),
            CourtFailure.UnknownTimeZone => (
                StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "That time zone is not recognised."),
            CourtFailure.UntrustedPhotoUrl => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.UntrustedAssetUrl,
                "A photo URL is not a secure link on the configured Cloudinary account."),
            CourtFailure.AlreadyUnderMaintenance => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This is already under maintenance. Lift the current closure first."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
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
