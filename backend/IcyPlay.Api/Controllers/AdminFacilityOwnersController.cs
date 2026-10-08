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
/// Admin-led facility owner onboarding. There is no self-service counterpart by
/// design: an owner exists only because someone on the platform team encoded
/// them.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/facility-owners")]
public sealed class AdminFacilityOwnersController(
    IFacilityOwnerOnboardingService onboarding,
    IFacilityOwnerEditService edits,
    IFacilityAttendantService attendants,
    IValidator<OnboardFacilityOwnerRequest> validator,
    IServiceProvider services,
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

        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await onboarding.OnboardAsync(request, actor, ct);
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
            OnboardingFailure.UntrustedContractDocument => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.UntrustedAssetUrl,
                "The signed agreement is not a secure link on the configured Cloudinary account."),
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

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var detail = await onboarding.GetAsync(id, ct);

        return detail is null
            ? NotFound(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.NotFound,
                "No facility owner with that id.")))
            : Ok(new ApiEnvelope<FacilityOwnerDetail>(detail));
    }

    /// <summary>
    /// Sends the owner a new activation link. Any link already outstanding stops
    /// working, so a resend cannot leave two live invitations behind it.
    /// </summary>
    [HttpPost("{id:guid}/resend-invitation")]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var sent = await onboarding.ResendInvitationAsync(id, actor, ct);

        return sent
            ? NoContent()
            : NotFound(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.NotFound,
                "No facility owner with that id.")));
    }

    [HttpPut("{id:guid}/business")]
    public Task<IActionResult> UpdateBusiness(Guid id, UpdateBusinessRequest request, CancellationToken ct) =>
        EditAsync(request, actor => edits.UpdateBusinessAsync(id, request, actor, ct), ct);

    [HttpPut("{id:guid}/facilities/{facilityId:guid}")]
    public Task<IActionResult> UpdateFacility(
        Guid id,
        Guid facilityId,
        UpdateFacilityRequest request,
        CancellationToken ct) =>
        EditAsync(request, actor => edits.UpdateFacilityAsync(id, facilityId, request, actor, ct), ct);

    [HttpPut("{id:guid}/facilities/{facilityId:guid}/hours")]
    public Task<IActionResult> UpdateOperatingHours(
        Guid id,
        Guid facilityId,
        UpdateOperatingHoursRequest request,
        CancellationToken ct) =>
        EditAsync(request, actor => edits.UpdateOperatingHoursAsync(id, facilityId, request, actor, ct), ct);

    /// <summary>
    /// Adds a term. Contracts are renewed, never rewritten: last year's term
    /// has to stay readable beside this year's, and platform fees will hang off
    /// a specific one.
    /// </summary>
    [HttpPost("{id:guid}/contracts")]
    public Task<IActionResult> RenewContract(Guid id, RenewContractRequest request, CancellationToken ct) =>
        EditAsync(request, actor => edits.RenewContractAsync(id, request, actor, ct), ct);

    /// <summary>
    /// Corrects the dates of a term. A start date typed wrong leaves an owner
    /// invisible to customers until it comes round, and the only way to see
    /// that is to be able to look at it.
    /// </summary>
    [HttpPut("{id:guid}/contracts/{contractId:guid}")]
    public Task<IActionResult> UpdateContractTerm(
        Guid id,
        Guid contractId,
        UpdateContractTermRequest request,
        CancellationToken ct) =>
        EditAsync(
            request,
            actor => edits.UpdateContractTermAsync(id, contractId, request, actor, ct),
            ct);

    /// <summary>
    /// What IcyPlay charges under one term. Its own endpoint because a rate is
    /// renegotiated far more often than a term is renewed, and going through a
    /// renewal would mean re-attaching the signed agreement to move a
    /// percentage.
    /// </summary>
    [HttpPut("{id:guid}/contracts/{contractId:guid}/rates")]
    public Task<IActionResult> UpdateContractRates(
        Guid id,
        Guid contractId,
        UpdateContractRatesRequest request,
        CancellationToken ct) =>
        EditAsync(
            request,
            actor => edits.UpdateContractRatesAsync(id, contractId, request, actor, ct),
            ct);

    /// <summary>
    /// Where the venue is paid, and how long it holds a court while waiting.
    /// Its own endpoint rather than part of the business details: a venue
    /// changes its GCash account or its hold without anything else about it
    /// changing, and a screen that saves everything at once puts the business
    /// name at risk to move a number.
    /// </summary>
    /// <summary>
    /// Who works a venue's desk. The owner is always on the list and has no id:
    /// they attend their own venues by owning them.
    /// </summary>
    [HttpGet("{id:guid}/facilities/{facilityId:guid}/attendants")]
    public async Task<IActionResult> Attendants(Guid id, Guid facilityId, CancellationToken ct)
    {
        var result = await attendants.ListAsync(id, facilityId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<FacilityAttendantDetail>>(result.Value!))
            : AttendantFailureResult(result.Failure);
    }

    /// <summary>
    /// What the console may do with an address, so the admin is told while
    /// they type rather than after they press save.
    /// </summary>
    [HttpGet("{id:guid}/facilities/{facilityId:guid}/attendants/check")]
    public async Task<IActionResult> CheckAttendantEmail(
        Guid id,
        Guid facilityId,
        [FromQuery] string email,
        CancellationToken ct)
    {
        var result = await attendants.CheckEmailAsync(id, facilityId, email, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<AttendantEmailCheck>(result.Value!))
            : AttendantFailureResult(result.Failure);
    }

    [HttpPost("{id:guid}/facilities/{facilityId:guid}/attendants")]
    public async Task<IActionResult> InviteAttendant(
        Guid id,
        Guid facilityId,
        InviteAttendantRequest request,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await attendants.InviteAsync(id, facilityId, request, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<FacilityAttendantDetail>(result.Value!))
            : AttendantFailureResult(result.Failure);
    }

    /// <summary>
    /// Sends an attendant who has not claimed their account another activation
    /// link. Any link already outstanding stops working.
    /// </summary>
    [HttpPost("{id:guid}/facilities/{facilityId:guid}/attendants/{attendantId:guid}/resend-invitation")]
    public async Task<IActionResult> ResendAttendantInvitation(
        Guid id,
        Guid facilityId,
        Guid attendantId,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await attendants.ResendInvitationAsync(id, facilityId, attendantId, actor, ct);

        return result.Succeeded ? NoContent() : AttendantFailureResult(result.Failure);
    }

    [HttpDelete("{id:guid}/facilities/{facilityId:guid}/attendants/{attendantId:guid}")]
    public async Task<IActionResult> RemoveAttendant(
        Guid id,
        Guid facilityId,
        Guid attendantId,
        [FromQuery] string? reason,
        CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await attendants.RemoveAsync(id, facilityId, attendantId, reason, actor, ct);

        return result.Succeeded ? NoContent() : AttendantFailureResult(result.Failure);
    }

    private static IActionResult AttendantFailureResult(AttendantFailure failure) =>
        AttendantFailureResults.ToResult(failure);

    [HttpPut("{id:guid}/payment-details")]
    public Task<IActionResult> UpdatePaymentDetails(
        Guid id,
        UpdatePaymentDetailsRequest request,
        CancellationToken ct) =>
        EditAsync(
            request,
            actor => edits.UpdatePaymentDetailsAsync(id, request, actor, ct),
            ct);

    /// <summary>
    /// How many days ahead the venue's courts can be booked, how many times one
    /// of its bookings may move, and how many days before it starts moves
    /// close. The venue can set all three from its own desk.
    /// </summary>
    [HttpPut("{id:guid}/booking-rules")]
    public Task<IActionResult> UpdateBookingRules(
        Guid id,
        UpdateBookingRulesRequest request,
        CancellationToken ct) =>
        EditAsync(
            request,
            actor => edits.UpdateBookingRulesAsync(id, request, actor, ct),
            ct);

    [HttpPut("{id:guid}/contracts/{contractId:guid}/document")]
    public Task<IActionResult> ReplaceContractDocument(
        Guid id,
        Guid contractId,
        ReplaceContractDocumentRequest request,
        CancellationToken ct) =>
        EditAsync(
            request,
            actor => edits.ReplaceContractDocumentAsync(id, contractId, request, actor, ct),
            ct);

    [HttpPost("{id:guid}/contracts/{contractId:guid}/cancel")]
    public Task<IActionResult> CancelContract(
        Guid id,
        Guid contractId,
        CancelContractRequest request,
        CancellationToken ct) =>
        EditAsync(request, actor => edits.CancelContractAsync(id, contractId, request, actor, ct), ct);

    /// <summary>Starts a signed term that has not begun yet, today.</summary>
    [HttpPost("{id:guid}/contracts/{contractId:guid}/activate")]
    public Task<IActionResult> ActivateContract(
        Guid id,
        Guid contractId,
        ActivateContractRequest request,
        CancellationToken ct) =>
        EditAsync(request, actor => edits.ActivateContractAsync(id, contractId, request, actor, ct), ct);

    /// <summary>
    /// Everything recorded against this owner, newest first, a page at a time.
    /// The console asks for the first page and the next one on "Load more".
    /// </summary>
    [HttpGet("{id:guid}/activity")]
    public async Task<IActionResult> ListActivity(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ActivityPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var paged = await edits.ListActivityAsync(id, page, pageSize, ct);

        return Ok(new ApiListEnvelope<ActivityEntry>(
            paged.Items,
            new PaginationMeta(paged.Page, paged.PageSize, paged.TotalItems, paged.TotalPages)));
    }

    private async Task<IActionResult> EditAsync<TRequest>(
        TRequest request,
        Func<AuditActor, Task<EditResult>> edit,
        CancellationToken ct)
    {
        var requestValidator = services.GetRequiredService<IValidator<TRequest>>();
        var validation = await requestValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.ValidationError,
                "The request is invalid.",
                validation.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray()))));
        }

        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        var result = await edit(actor);
        if (result.Succeeded)
        {
            return NoContent();
        }

        var (status, code, message) = result.Failure switch
        {
            EditFailure.NotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That record does not exist, or does not belong to this facility owner."),
            EditFailure.UnknownAmenity => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "One of the selected amenities no longer exists."),
            EditFailure.UnknownTimeZone => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That time zone is not recognised."),
            EditFailure.AlreadyCancelled => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This contract has already been cancelled."),
            EditFailure.UntrustedPhotoUrl => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.UntrustedAssetUrl,
                "A photo URL is not a secure link on the configured Cloudinary account."),
            EditFailure.UntrustedContractDocument => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.UntrustedAssetUrl,
                "The agreement is not a secure link on the configured Cloudinary account."),
            EditFailure.OverlappingContract => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This term overlaps a contract that is still live. Cancel it first, or pick later dates."),
            EditFailure.AlreadyStarted => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This term has already started, so there is nothing to bring forward."),
            _ => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The change could not be saved.")
        };

        logger.LogWarning("Facility owner edit rejected: {Failure}", result.Failure);
        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
    }

    /// <summary>
    /// The address and the user agent are HTTP facts, so the audit actor is
    /// assembled here rather than reaching for them inside the service.
    /// </summary>
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
