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

    private IActionResult Failure(PlatformReportFailure failure)
    {
        var message = failure switch
        {
            PlatformReportFailure.OwnerNotFound => "There is no facility owner with that id.",
            PlatformReportFailure.VenueNotFound => "There is no venue with that id for that owner.",
            _ => "The request could not be completed."
        };

        return NotFound(new ApiErrorEnvelope(new ApiError(ErrorCodes.NotFound, message)));
    }
}
