using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The venue's desk: payments waiting to be checked, and the two answers
/// somebody standing at it can give.
///
/// Open to owners and attendants alike. An owner attends every venue they own
/// and an attendant the ones they are put on, and which of those a caller is
/// looking at is worked out from who they are rather than from anything they
/// send.
/// </summary>
[ApiController]
[Authorize(Roles = $"{UserRoleName.FacilityOwner},{UserRoleName.FacilityAttendant}")]
[Route("api/v1/desk")]
public sealed class DeskController(IDeskService desk) : ControllerBase
{
    /// <summary>
    /// The venues this person may confirm bookings for. One venue needs no
    /// filter; five do.
    /// </summary>
    [HttpGet("venues")]
    public async Task<IActionResult> Venues(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        return Ok(new ApiEnvelope<IReadOnlyCollection<DeskVenue>>(
            await desk.VenuesAsync(userId, ct)));
    }

    [HttpGet("bookings")]
    public async Task<IActionResult> Bookings(
        [FromQuery] string tab = DeskTab.Waiting,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.ListAsync(
            userId,
            new DeskQuery(tab, facilityId, page, pageSize),
            ct);

        if (!result.Succeeded)
        {
            return Failure(result.Failure);
        }

        var paged = result.Value!;

        return Ok(new ApiListEnvelope<DeskBooking>(
            paged.Items,
            new PaginationMeta(paged.Page, paged.PageSize, paged.TotalItems, paged.TotalPages)));
    }

    /// <summary>
    /// The courts these venues have registered, each with the parts it is sold
    /// in. The diary is organised by these.
    /// </summary>
    [HttpGet("courts")]
    public async Task<IActionResult> Courts(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        return Ok(new ApiEnvelope<IReadOnlyCollection<DeskCourt>>(
            await desk.CourtsAsync(userId, ct)));
    }

    /// <summary>
    /// Every booked hour on one court between two dates. Thin: what a calendar
    /// needs to draw a square, and no more.
    /// </summary>
    [HttpGet("courts/{courtId:guid}/schedule")]
    public async Task<IActionResult> Schedule(
        Guid courtId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.ScheduleAsync(userId, courtId, from, to, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<ScheduleEntry>>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// One court's bookings as a list. Answers for what fell through as well as
    /// what stands, which is what a list is for.
    /// </summary>
    [HttpGet("courts/{courtId:guid}/bookings")]
    public async Task<IActionResult> CourtBookings(
        Guid courtId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.CourtBookingsAsync(
            userId,
            new CourtBookingQuery(courtId, from, to, status, page, pageSize),
            ct);

        if (!result.Succeeded)
        {
            return Failure(result.Failure);
        }

        var paged = result.Value!;

        return Ok(new ApiListEnvelope<DeskBooking>(
            paged.Items,
            new PaginationMeta(paged.Page, paged.PageSize, paged.TotalItems, paged.TotalPages)));
    }

    /// <summary>One booking in full, for an hour somebody has clicked.</summary>
    [HttpGet("bookings/{bookingId:guid}")]
    public async Task<IActionResult> Booking(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.BookingAsync(userId, bookingId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskBooking>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Says the payment is good. The customer is emailed their confirmation.
    /// </summary>
    [HttpPost("bookings/{bookingId:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId || CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await desk.ConfirmAsync(userId, bookingId, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskBooking>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Says it is not, with a reason. The hours go back on sale.</summary>
    [HttpPost("bookings/{bookingId:guid}/reject")]
    public async Task<IActionResult> Reject(
        Guid bookingId,
        RejectBookingRequest? request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId || CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await desk.RejectAsync(userId, bookingId, request?.Reason, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskBooking>(result.Value!))
            : Failure(result.Failure);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    /// <summary>
    /// Who pressed the button, for the trail. The role recorded is the one that
    /// let them in, so an owner confirming and an attendant confirming read
    /// differently later.
    /// </summary>
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

    private IActionResult Failure(DeskFailure failure)
    {
        var (status, code, message) = failure switch
        {
            DeskFailure.NotAttended => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That venue is not one you work."),
            DeskFailure.BookingNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That booking is not at a venue you work."),
            DeskFailure.NotWaiting => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That booking has already been decided. Refresh to see where it stands."),
            DeskFailure.NoReceipt => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "There is no receipt on that booking to check."),
            DeskFailure.UnknownTab => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for either the waiting bookings or the confirmed ones."),
            DeskFailure.UnknownStatus => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That is not a booking status."),
            DeskFailure.WindowTooWide => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for a stretch of six weeks or less."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
    }
}
