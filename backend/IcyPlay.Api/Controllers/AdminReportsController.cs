using IcyPlay.Api.Common;
using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The venue desk's reports, for the platform admin: across every venue, or
/// narrowed to one facility owner, or one venue.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/reports")]
public sealed class AdminReportsController(IPlatformReportService reports) : ControllerBase
{
    /// <summary>Every facility owner and their venues, for the filters.</summary>
    [HttpGet("owners")]
    public async Task<IActionResult> Owners(CancellationToken ct) =>
        Ok(new ApiEnvelope<IReadOnlyCollection<ReportOwner>>(await reports.OwnersAsync(ct)));

    /// <summary>
    /// The platform this minute: the desk's five numbers across every venue in
    /// scope, and owner by owner.
    /// </summary>
    [HttpGet("snapshot")]
    public async Task<IActionResult> Snapshot(
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.SnapshotAsync(facilityOwnerId, facilityId, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<PlatformSnapshot>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// Court utilisation across every venue, one owner's, or one venue — the
    /// desk's own report, with the rental in it.
    /// </summary>
    [HttpGet("court-utilization")]
    public async Task<IActionResult> CourtUtilization(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.UtilizationAsync(facilityOwnerId, facilityId, from, to, ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<UtilizationReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>
    /// The utilisation figures cut by date, court by court — Sold Hours, and
    /// the trends on Sold Courts and Not Sold Courts.
    /// </summary>
    [HttpGet("hours-over-time")]
    public async Task<IActionResult> HoursOverTime(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.HoursOverTimeAsync(
            facilityOwnerId,
            facilityId,
            new HoursQuery(from, to, grain),
            ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<HoursOverTime>(result.Value!))
            : Failure(result.Failure);
    }

    // The desk's other reports, across the scope. Each is the desk's own,
    // handed the owner's or venue's venues instead of the desk's.

    /// <summary>Moved Bookings.</summary>
    [HttpGet("moves")]
    public async Task<IActionResult> Moves(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.MovesAsync(facilityOwnerId, facilityId, new HoursQuery(from, to, grain), ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MovesReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Declined Bookings.</summary>
    [HttpGet("declines")]
    public async Task<IActionResult> Declines(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.DeclinesAsync(facilityOwnerId, facilityId, new HoursQuery(from, to, grain), ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<DeclinesReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Takings, up to five years.</summary>
    [HttpGet("takings")]
    public async Task<IActionResult> Takings(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.TakingsAsync(facilityOwnerId, facilityId, new HoursQuery(from, to, grain), ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<TakingsReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Missed Income.</summary>
    [HttpGet("missed")]
    public async Task<IActionResult> Missed(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string grain = HoursGrain.Day,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.MissedAsync(facilityOwnerId, facilityId, new HoursQuery(from, to, grain), ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<MissedReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Court Changes, optionally narrowed to one court.</summary>
    [HttpGet("court-changes")]
    public async Task<IActionResult> CourtChanges(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] Guid? courtId = null,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.CourtChangesAsync(facilityOwnerId, facilityId, new CourtChangesQuery(from, to, CourtId: courtId), ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CourtChangesReport>(result.Value!))
            : Failure(result.Failure);
    }

    /// <summary>Court Mix, retired courts listed when asked for.</summary>
    [HttpGet("court-mix")]
    public async Task<IActionResult> CourtMix(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] bool includeRetired = false,
        [FromQuery] Guid? facilityOwnerId = null,
        [FromQuery] Guid? facilityId = null,
        CancellationToken ct = default)
    {
        var result = await reports.CourtMixAsync(facilityOwnerId, facilityId, new CourtMixQuery(from, to, IncludeRetired: includeRetired), ct);

        return result.Succeeded
            ? Ok(new ApiEnvelope<CourtMixReport>(result.Value!))
            : Failure(result.Failure);
    }

    private IActionResult Failure(PlatformReportFailure failure)
    {
        var (status, code, message) = failure switch
        {
            PlatformReportFailure.OwnerNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "There is no facility owner with that id."),
            PlatformReportFailure.VenueNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "There is no venue with that id for that owner."),
            PlatformReportFailure.WindowBackwards => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The report has to end on or after it starts."),
            PlatformReportFailure.WindowTooWide => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for a year or less."),
            PlatformReportFailure.TakingsWindowTooWide => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for five years or less."),
            PlatformReportFailure.CourtNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "There is no court with that id at those venues."),
            PlatformReportFailure.UnknownGrain => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for it by day, week, month, quarter, half or year."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message)));
    }
}
