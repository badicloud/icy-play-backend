using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// Taking and reading a customer's own bookings.
///
/// Availability sits here too but is anonymous: a visitor should be able to see
/// whether Saturday morning is free before being asked to make an account. Sign
/// in is the price of holding an hour, not of looking at one.
///
/// Booking a court is a customer's to do, and the role sits on each of those
/// actions. Reading your own bookings is not: it is anybody's right to their
/// own record, and an account with nothing on it should be told so rather than
/// turned away. The role cannot be relaxed per action — authorization attributes
/// add up — so it belongs where it applies rather than over everything.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/bookings")]
public sealed class BookingsController(IBookingService bookings) : ControllerBase
{
    /// <summary>
    /// What can be booked on one court on one date, hour by hour, with what
    /// each hour costs and why.
    /// </summary>
    [HttpGet("/api/v1/catalog/bookable-courts/{bookableCourtId:guid}/availability")]
    [AllowAnonymous]
    public async Task<IActionResult> Availability(
        Guid bookableCourtId,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        // Deliberately not cached. A grid that is a minute stale is a customer
        // picking an hour that has just gone.
        Response.Headers.CacheControl = "no-store";

        var result = await bookings.AvailabilityAsync(bookableCourtId, date, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<AvailabilityDay>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Every date in the booking window, counted rather than priced, so the day
    /// picker can grey out what cannot be taken whole.
    /// </summary>
    [HttpGet("/api/v1/catalog/bookable-courts/{bookableCourtId:guid}/day-outlook")]
    [AllowAnonymous]
    public async Task<IActionResult> DayOutlook(Guid bookableCourtId, CancellationToken ct)
    {
        // Same reason as the grid: a picker a minute stale offers a day that
        // has just been taken.
        Response.Headers.CacheControl = "no-store";

        var result = await bookings.OutlookAsync(bookableCourtId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<Application.Bookings.DayOutlook>>(result.Value!))
            : Failure(result.Failure);
    }

    [HttpPost]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.CreateAsync(request, userId, ct);

        return result.Succeeded
            ? CreatedAtAction(
                nameof(Get),
                new
                {
                    bookingId = result.Value!.Id
                },
                new ApiEnvelope<BookingDetail>(result.Value))
            : Failure(result.Failure);
    }

    /// <summary>
    /// One booking of the caller's own.
    ///
    /// Any signed-in account, not customers only: the query is scoped to the
    /// caller either way, and somebody who works a venue's desk is still a
    /// person who can be told what is on their own account.
    /// </summary>
    [HttpGet("{bookingId:guid}")]
    public async Task<IActionResult> Get(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.GetAsync(bookingId, userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<BookingDetail>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Every booking the caller has made. Empty is an answer.</summary>
    [HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        return Ok(new ApiEnvelope<IReadOnlyCollection<BookingDetail>>(
            await bookings.ListForCustomerAsync(userId, ct)));
    }

    /// <summary>
    /// What moving onto that court would cost. Answered before anybody commits
    /// to anything, because a move that wants paying for is a different
    /// proposition from one that does not.
    /// </summary>
    [HttpGet("{bookingId:guid}/move-quote")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> MoveQuote(
        Guid bookingId,
        [FromQuery] Guid toBookableCourtId,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.QuoteMoveAsync(bookingId, userId, toBookableCourtId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MoveQuoteResponse>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Asks to move a booking onto another court, keeping its hours. Raises a
    /// request; the booking itself does not move until the venue confirms.
    /// </summary>
    [HttpPost("{bookingId:guid}/move")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> Move(
        Guid bookingId,
        MoveBookingRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.MoveAsync(bookingId, userId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<BookingDetail>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The GCash receipt for the difference an upgrade came to. The hold's
    /// clock stops here: from now on the wait is the venue's.
    /// </summary>
    [HttpPost("{bookingId:guid}/move/receipt")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> AttachMoveReceipt(
        Guid bookingId,
        AttachReceiptRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.AttachMoveReceiptAsync(bookingId, userId, request.ReceiptUrl, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<BookingDetail>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Thought better of it. The held court goes back on sale.</summary>
    [HttpPost("{bookingId:guid}/move/withdraw")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> WithdrawMove(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.WithdrawMoveAsync(bookingId, userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<BookingDetail>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Records the receipt the browser has just put in Cloudinary. The file
    /// never passes through here — only the link to it, which is checked.
    /// </summary>
    [HttpPost("{bookingId:guid}/receipt")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> AttachReceipt(
        Guid bookingId,
        AttachReceiptRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.AttachReceiptAsync(bookingId, userId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<BookingDetail>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Hands the booking to the venue to check, and writes to both sides.
    /// </summary>
    [HttpPost("{bookingId:guid}/submit-payment")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> SubmitPayment(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.SubmitForVerificationAsync(bookingId, userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<BookingDetail>(result.Value!))
            : Failure(result.Failure);
    }

    [HttpPost("{bookingId:guid}/cancel")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> Cancel(
        Guid bookingId,
        CancelBookingRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.CancelAsync(bookingId, userId, request?.Reason, ct);

        return result.Succeeded ? NoContent() : Failure(result.Failure);
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private IActionResult Failure(BookingFailure failure)
    {
        var (status, code, message) = failure switch
        {
            BookingFailure.CourtNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That court is not taking bookings."),
            BookingFailure.UnderMaintenance => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This court is closed for maintenance on that date."),
            BookingFailure.SlotTaken => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "One of those hours has just been taken. Refresh and pick again."),
            BookingFailure.DayNotWhollyAvailable => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Part of that day is already booked, so it cannot be hired whole. Book by the hour instead."),
            BookingFailure.NotPriced => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "This venue has not set a price for that sport yet."),
            BookingFailure.OutsideOpeningHours => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "One of those hours falls outside the times this court is open."),
            BookingFailure.BelowMinimumDuration => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That is shorter than this venue's minimum booking."),
            BookingFailure.DatesNotConsecutive => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The days in a multi-day booking have to run one after another, apart from days the venue is closed."),
            BookingFailure.KindDoesNotMatchSlots => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The hours sent do not match the kind of booking asked for."),
            BookingFailure.DateInThePast => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That date has already gone."),
            BookingFailure.NotMovable => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Only a booking the venue is holding or has confirmed can be moved."),
            BookingFailure.MoveLimitReached => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking has been moved as many times as this venue allows."),
            BookingFailure.BookingFinished => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Every hour of this booking has been played, so there is nothing left to move."),
            BookingFailure.MoveAlreadyRequested => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking is already waiting on a move. Settle that one first."),
            BookingFailure.NotTheSameOffering => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "A booking can only move to another court for the same sport at the same venue."),
            BookingFailure.MoveRequestNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "No move is waiting on this booking."),
            BookingFailure.MoveNotPaid => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The difference has not been paid yet, so there is nothing to confirm."),
            BookingFailure.TooFarAhead => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                $"Courts can be booked up to {BookingWindow.DaysAhead} days ahead."),
            BookingFailure.TooManySlots => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That is more hours than one booking can hold."),
            BookingFailure.HoldExpired => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking was not paid for in time, so the hours went back on sale."),
            BookingFailure.UntrustedReceiptUrl => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.UntrustedAssetUrl,
                "That receipt is not a secure link on the configured Cloudinary account."),
            BookingFailure.ReceiptNotAnImage => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The receipt has to be a picture — a screenshot or a photo of your GCash confirmation."),
            BookingFailure.NoReceipt => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Upload your GCash receipt before submitting it."),
            BookingFailure.NotAwaitingPayment => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking is not waiting to be paid for."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
    }
}

public sealed record CancelBookingRequest(string? Reason);
