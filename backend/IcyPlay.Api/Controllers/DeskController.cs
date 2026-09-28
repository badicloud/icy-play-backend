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
public sealed class DeskController(IDeskService desk, IBookingService bookings) : ControllerBase
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
    /// Everything that has happened to one booking, newest first — the same
    /// account the customer reads of it.
    /// </summary>
    [HttpGet("bookings/{bookingId:guid}/history")]
    public async Task<IActionResult> History(Guid bookingId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.HistoryAsync(userId, bookingId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<BookingHistoryEntry>>(result.Value!))
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

    /// <summary>
    /// The attendants at the venues this person owns, and whether each may read
    /// the money. Owners only.
    /// </summary>
    [HttpGet("attendants")]
    public async Task<IActionResult> Attendants(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.AttendantsAsync(userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<DeskAttendant>>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Lets one attendant read the venue's money, or stops them. Owners only.</summary>
    [HttpPut("attendants/{attendantId:guid}/money")]
    public async Task<IActionResult> SetAttendantMoney(
        Guid attendantId,
        SetAttendantMoneyRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId || CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await desk.SetAttendantMoneyAsync(userId, attendantId, request.CanSeeMoney, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskAttendant>(result.Value!))
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

        var result = await desk.RejectAsync(
            userId,
            bookingId,
            request ?? new RejectBookingRequest(null),
            actor,
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskBooking>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The utilization figures cut by date rather than totalled per court: one
    /// row per court per period, which a chart folds into one line or five and
    /// a table prints as it stands.
    ///
    /// No money in it, so an attendant reads the same answer an owner does.
    /// </summary>
    [HttpGet("reports/hours-over-time")]
    public async Task<IActionResult> HoursOverTime(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.HoursOverTimeAsync(
            userId,
            new HoursQuery(from, to, grain, facilityId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<HoursOverTime>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// What the venue has right now: courts by venue type, and the sports and
    /// events each is set up for, with how much of each venue type's open hours
    /// sold in the range. Retired courts are listed only when asked for.
    ///
    /// Open to attendants for now, the same as the other reports.
    /// </summary>
    [HttpGet("reports/court-mix")]
    public async Task<IActionResult> CourtMix(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] bool includeRetired = false,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.CourtMixAsync(
            userId,
            new CourtMixQuery(from, to, facilityId, includeRetired),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CourtMixReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Every change made to the venue's courts in a range, newest first: what
    /// it was, what it became, who changed it and why. Read back out of the
    /// audit trail, which already records all of it.
    ///
    /// Open to attendants for now, the same as the other reports.
    /// </summary>
    [HttpGet("reports/court-changes")]
    public async Task<IActionResult> CourtChanges(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] Guid? courtId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.CourtChangesAsync(
            userId,
            new CourtChangesQuery(from, to, facilityId, courtId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CourtChangesReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// What the venue's open, unsold hours would have earned at its own rates:
    /// per period, per court, and per sport court. Only hours that have begun —
    /// one still ahead can still be sold.
    ///
    /// Open to attendants for now, the same as takings.
    /// </summary>
    [HttpGet("reports/missed")]
    public async Task<IActionResult> Missed(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Week,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.MissedAsync(
            userId,
            new HoursQuery(from, to, grain, facilityId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MissedReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// What customers paid the venue, period by period and court by court: a
    /// booking's payment on the day the desk confirmed it, an upgrade's balance
    /// on the day the desk approved it, and the platform fee inside it set
    /// apart.
    ///
    /// Open to attendants for now. Who may see money is a permission still to
    /// be built, and until then the desk shows it to whoever works it.
    /// </summary>
    [HttpGet("reports/takings")]
    public async Task<IActionResult> Takings(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.TakingsAsync(
            userId,
            new HoursQuery(from, to, grain, facilityId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<TakingsReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// How many payments the desk turned down, against how many it checked, and
    /// the reasons it gave — with the refusals themselves, newest first.
    ///
    /// The amounts are in it for attendants as well as owners, for now: it is
    /// the desk's own work being counted, and what was sent is on the receipt
    /// they already looked at.
    /// </summary>
    [HttpGet("reports/declines")]
    public async Task<IActionResult> Declines(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.DeclinesAsync(
            userId,
            new HoursQuery(from, to, grain, facilityId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeclinesReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// How many bookings customers moved, period by period, and the reasons
    /// they gave — with the moves themselves, newest first.
    ///
    /// No money in it, so an attendant reads the same answer an owner does.
    /// </summary>
    [HttpGet("reports/moves")]
    public async Task<IActionResult> Moves(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.MovesAsync(
            userId,
            new HoursQuery(from, to, grain, facilityId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MovesReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// What the venue looks like at this moment: how many courts, how many
    /// parts they are sold in, and how many of those have somebody on them.
    ///
    /// Read on each venue's own clock, so a desk asking at nine in the morning
    /// is answered about nine in the morning wherever the building is.
    /// </summary>
    [HttpGet("reports/snapshot")]
    public async Task<IActionResult> Snapshot(
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.SnapshotAsync(userId, facilityId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<VenueSnapshot>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// How much of what the venue had open actually got used, court by court.
    ///
    /// Open to attendants as well as owners, without the money: knowing which
    /// courts sit empty on a Wednesday is the desk's own business, and what the
    /// venue took is not. The rental is left out of an attendant's response
    /// rather than hidden by the page.
    /// </summary>
    [HttpGet("reports/court-utilization")]
    public async Task<IActionResult> CourtUtilization(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.UtilizationAsync(
            userId,
            new UtilizationQuery(from, to, facilityId),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<UtilizationReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Upgrades customers have paid for and handed over, and the ones already
    /// settled.
    /// </summary>
    [HttpGet("upgrades")]
    public async Task<IActionResult> Upgrades(
        [FromQuery] string tab = DeskUpgradeTab.Waiting,
        [FromQuery] Guid? facilityId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.UpgradesAsync(
            userId,
            new DeskUpgradeQuery(tab, facilityId, page, pageSize),
            ct);

        if (!result.Succeeded)
        {
            return Failure(result.Failure);
        }

        var paged = result.Value!;

        return Ok(new ApiListEnvelope<DeskUpgrade>(
            paged.Items,
            new PaginationMeta(paged.Page, paged.PageSize, paged.TotalItems, paged.TotalPages)));
    }

    /// <summary>
    /// Says the payment is good and moves the booking onto the better court.
    /// </summary>
    [HttpPost("upgrades/{upgradeId:guid}/approve")]
    public async Task<IActionResult> ApproveUpgrade(Guid upgradeId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId || CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await desk.ApproveUpgradeAsync(userId, upgradeId, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskUpgrade>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Says no, with a reason. The booking stays exactly where it was.</summary>
    [HttpPost("upgrades/{upgradeId:guid}/decline")]
    public async Task<IActionResult> DeclineUpgrade(
        Guid upgradeId,
        DeclineUpgradeRequest? request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId || CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await desk.DeclineUpgradeAsync(userId, upgradeId, request?.Reason, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskUpgrade>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>What this venue has set for itself.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.SettingsAsync(userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskSettings>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Every change to the venue's dials, newest first, whoever made it — the
    /// desk, or the platform on the venue's behalf.
    /// </summary>
    [HttpGet("settings/history")]
    public async Task<IActionResult> SettingsHistory(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var result = await desk.SettingsHistoryAsync(userId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<IReadOnlyCollection<DeskSettingsChange>>(result.Value!))
            : Failure(result.Failure);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        UpdateDeskSettingsRequest request,
        CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId || CurrentActor() is not AuditActor actor)
        {
            return Unauthorized();
        }

        var result = await desk.UpdateSettingsAsync(userId, request, actor, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeskSettings>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The booking service answers with its own failures, which are not the
    /// desk's. Only the ones an attendant can actually provoke are spelled out.
    /// </summary>
    private IActionResult BookingFailureResult(BookingFailure failure)
    {
        var (status, code, message) = failure switch
        {
            BookingFailure.CourtNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "No booking with that id."),
            BookingFailure.NotMovable => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Only a booking the venue is holding or has confirmed can be moved."),
            BookingFailure.BookingFinished => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Every hour of this booking has been played, so there is nothing left to move."),
            BookingFailure.MoveAlreadyRequested => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This booking is already waiting on a move. Settle that one first."),
            BookingFailure.MoveRequestNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "No move is waiting on this booking."),
            BookingFailure.MoveNotPaid => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That court costs more than this booking has paid. Ask the customer to upgrade, or waive the difference and say why."),
            BookingFailure.SlotTaken => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Somebody already has one of those hours on that court."),
            BookingFailure.OutsideOpeningHours => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That court is not open for all of those hours."),
            BookingFailure.NotPriced => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That court has no price set, so nothing can be moved onto it."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "That move could not be made.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
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
            DeskFailure.UpgradeNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That upgrade is not one of yours to decide."),
            DeskFailure.UpgradeNotWaiting => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Somebody has already decided this one."),
            DeskFailure.UpgradeHoursTaken => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Those hours have been taken since this was asked for, so this upgrade " +
                "cannot go ahead. Decline it and the customer keeps the booking they have."),
            DeskFailure.UpgradeStale => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Hours on this booking have been played since the upgrade was asked for, " +
                "so it no longer adds up. Decline it and ask the customer to choose again."),
            DeskFailure.WindowTooWide => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for a stretch of six weeks or less."),
            DeskFailure.ReportWindowTooWide => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for a year or less."),
            DeskFailure.MoneyHidden => (
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Your venue's owner has not shared its money figures with you."),
            DeskFailure.NotOwner => (
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Only the venue's owner can do that."),
            DeskFailure.AttendantNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That attendant is not at a venue you own."),
            DeskFailure.TakingsWindowTooWide => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for five years or less."),
            DeskFailure.WindowBackwards => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The report has to end on or after it starts."),
            DeskFailure.UnknownGrain => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for it by day, by week or by month."),
            DeskFailure.RejectReasonRequired => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Pick why you are turning this booking down."),
            DeskFailure.RejectNoteRequired => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Say a few words about what was wrong."),
            DeskFailure.RejectNoteTooLong => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Keep the note to 200 characters or fewer."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
    }
}
