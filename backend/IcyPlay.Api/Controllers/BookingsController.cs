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
    /// Everything that has happened to this booking, newest first — what just
    /// happened is what somebody opens a history for, and putting it at the
    /// bottom makes them scroll past everything they already knew.
    /// </summary>
    [HttpGet("{bookingId:guid}/history")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> History(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.HistoryAsync(bookingId, userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<BookingHistoryEntry>>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The hours a move could be placed on, on one date, before any court has
    /// been chosen.
    ///
    /// The move screen asks date, then hours, then court — which leaves a step
    /// with no court to ask about, so the hours come from the building rather
    /// than from any floor in it.
    /// </summary>
    [HttpGet("{bookingId:guid}/move-window")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> MoveWindow(
        Guid bookingId,
        [FromQuery] DateOnly date,
        CancellationToken ct)
    {
        // Same reason as the hour grid: a window a minute stale offers an hour
        // that has just gone.
        Response.Headers.CacheControl = "no-store";

        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.MoveWindowAsync(bookingId, userId, date, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MoveWindow>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Every court this booking could be moved onto at those hours, priced.
    ///
    /// A POST for something that changes nothing, for the same reason the
    /// quote is one: the hours being asked about are a list, and a list
    /// belongs in a body rather than strung through a query.
    /// </summary>
    [HttpPost("{bookingId:guid}/move-options")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> MoveOptions(
        Guid bookingId,
        MoveOptionsRequest request,
        CancellationToken ct)
    {
        // A list of free courts is the most perishable thing this API says.
        Response.Headers.CacheControl = "no-store";

        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.MoveOptionsAsync(bookingId, userId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MoveOptions>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// What moving onto that court, at those hours, would come to — and whether
    /// the booking falls on the venue's today, which is what the move screen
    /// needs before it can offer dates.
    ///
    /// A POST for something that changes nothing, because the hours being asked
    /// about are a list: a quote for a whole proposed booking belongs in a body
    /// rather than strung through a query.
    /// </summary>
    [HttpPost("{bookingId:guid}/move-quote")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> MoveQuote(
        Guid bookingId,
        MoveBookingRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.QuoteMoveAsync(
            bookingId,
            userId,
            request.ToBookableCourtId,
            request.Slots,
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MoveQuoteResponse>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Moves a booking onto another court, and onto other hours when they are
    /// given. It happens at once — nobody is asked to approve it.
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
    /// Asks to move onto hours that cost more, and offers to pay the
    /// difference. The booking does not move: this writes the request down,
    /// holds the hours on a clock, and sends the customer to pay.
    /// </summary>
    [HttpPost("{bookingId:guid}/upgrade")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> RequestUpgrade(
        Guid bookingId,
        MoveBookingRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.RequestUpgradeAsync(bookingId, userId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<UpgradeRequestResponse>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The upgrade still open on this booking, or nothing when there is none.
    ///
    /// What the upgrade screen reads on every visit, so a refresh or a second
    /// tab lands on the step the customer is actually at. Nothing open is an
    /// ordinary answer rather than a not-found: the question is what is
    /// waiting, and "nothing" is a complete reply.
    /// </summary>
    [HttpGet("{bookingId:guid}/upgrade")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> OpenUpgrade(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        // A hold is counted in minutes. A cached answer is a customer watching
        // a clock that stopped.
        Response.Headers.CacheControl = "no-store";

        var result = await bookings.OpenUpgradeAsync(bookingId, userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<UpgradeRequestResponse?>(result.Value))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Records the receipt for an upgrade the customer has paid. The file never
    /// passes through here — only the link to it, which is checked.
    /// </summary>
    [HttpPost("{bookingId:guid}/upgrade/receipt")]
    [Authorize(Roles = UserRoleName.Customer)]
    public async Task<IActionResult> AttachUpgradeReceipt(
        Guid bookingId,
        AttachReceiptRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await bookings.AttachUpgradeReceiptAsync(bookingId, userId, request, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<UpgradeRequestResponse>(result.Value!))
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
                "A run of days can pass over days it cannot be sold, but not over a day that is free to book."),
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
                "Only a booking the venue has confirmed can be moved."),
            BookingFailure.MoveLimitReached => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking has been moved as many times as this venue allows."),
            BookingFailure.MoveReasonRequired => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Tell us why you are moving this booking."),
            BookingFailure.MoveReasonNoteRequired => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Say a few words about why you are moving it."),
            BookingFailure.MoveReasonNoteTooLong => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Keep the reason to 200 characters or fewer."),
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
            BookingFailure.VenueCannotBePaid => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This venue has not set up a way to be paid yet, so a receipt cannot be sent. " +
                "Please contact them to arrange payment."),
            BookingFailure.NothingWouldChange => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That is the court and the hours you already have, so there is nothing to move."),
            BookingFailure.DayBookingInPlay => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking runs for the whole day and that day has started, " +
                "so it stays on the court it is on."),
            BookingFailure.NothingToUpgrade => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Those hours cost the same or less, so there is nothing to pay. " +
                "Ask the venue to move you onto them instead."),
            BookingFailure.MoveCostsMore => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That court costs more than this booking has been paid for. " +
                "Upgrade to it and pay the difference instead."),
            BookingFailure.TooLateToMove => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking starts too soon to be moved. " +
                "The venue stops taking moves a set number of days before a booking starts."),
            BookingFailure.CourtOnlyOnceStarted => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking has started, so its hours stay as they are. You can still ask to change court."),
            BookingFailure.MoveRequestNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "No move is waiting on this booking. The hold may have run out, " +
                "in which case those hours are back on sale."),
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
