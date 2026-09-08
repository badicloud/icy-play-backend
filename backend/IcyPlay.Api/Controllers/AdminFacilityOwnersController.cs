using System.Security.Claims;
using FluentValidation;
using IcyPlay.Api.Common;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// Admin-led facility owner onboarding. There is no self-service counterpart by
/// design: an owner exists only because someone on the platform team encoded
/// them.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/facility-owners")]
public sealed class AdminFacilityOwnersController(
    IFacilityOwnerOnboardingService onboarding,
    IValidator<OnboardFacilityOwnerRequest> validator,
    ILogger<AdminFacilityOwnersController> logger) : ControllerBase
{
    /// <summary>
    /// The whole wizard in one call: owner account, business, documents, the
    /// first facility with its hours and amenities, and the contract that makes
    /// it bookable. One transaction, so an abandoned wizard leaves nothing
    /// half-built behind it.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Onboard(OnboardFacilityOwnerRequest request, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            var details = validation.Errors
                .GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());

            logger.LogWarning(
                "Facility owner onboarding failed validation for fields: {ValidationFields}",
                string.Join(", ", details.Keys));

            return BadRequest(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.ValidationError, "The request is invalid.", details)));
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId))
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await onboarding.OnboardAsync(request, adminUserId, ct);
        if (result.Succeeded)
        {
            return StatusCode(
                StatusCodes.Status201Created,
                new ApiEnvelope<OnboardedFacilityOwnerResponse>(result.Value));
        }

        var (status, code, message) = result.Failure switch
        {
            OnboardingFailure.DuplicateEmail => (
                StatusCodes.Status409Conflict,
                ErrorCodes.EmailAlreadyExists,
                "An account already uses this email address."),
            OnboardingFailure.DuplicateSlug => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That facility name is already taken. Try a more specific one."),
            OnboardingFailure.UnknownAmenity => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "One of the selected amenities no longer exists."),
            OnboardingFailure.UntrustedAssetUrl => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.UntrustedAssetUrl,
                "A document URL is not a secure link on the configured Cloudinary account."),
            OnboardingFailure.UnknownTimeZone => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That time zone is not recognised."),
            _ => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The onboarding could not be completed.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken ct = default)
    {
        var result = await onboarding.ListAsync(
            new FacilityOwnerQuery(search, status, page, pageSize, sortBy, sortDirection),
            ct);

        if (result is null)
        {
            return BadRequest(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.InvalidSortField,
                "Sort by businessName or createdAt.")));
        }

        return Ok(new ApiListEnvelope<FacilityOwnerListItem>(
            result.Items,
            new PaginationMeta(result.Page, result.PageSize, result.TotalItems, result.TotalPages)));
    }
}
