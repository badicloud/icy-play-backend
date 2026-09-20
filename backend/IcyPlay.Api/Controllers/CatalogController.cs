using IcyPlay.Api.Common;
using IcyPlay.Application.Facilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// What the public site can offer. Anonymous, because the landing page is the
/// first thing a visitor sees and asking them to sign in to find out whether
/// anyone plays badminton nearby would be the wrong way round.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/catalog")]
public sealed class CatalogController(IActivityCatalog catalog) : ControllerBase
{
    /// <summary>
    /// The sports and events with a court actually configured for them. Held
    /// briefly by the browser as well as the server: the listing changes when
    /// an admin sets a court up, not between one visitor and the next.
    /// </summary>
    [HttpGet("activities")]
    public async Task<IActionResult> Activities(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=60";

        return Ok(new ApiEnvelope<IReadOnlyCollection<CatalogActivity>>(
            await catalog.ListAsync(ct)));
    }

    /// <summary>
    /// Every court on offer, or just those for one sport. A court divided three
    /// ways appears three times, because three games can run on it at once and
    /// each is booked on its own.
    /// </summary>
    /// <summary>
    /// Every venue on offer, with what it has and where it is. What the landing
    /// page lists, because a venue is what somebody chooses first.
    /// </summary>
    [HttpGet("facilities")]
    [AllowAnonymous]
    public async Task<IActionResult> Facilities(CancellationToken ct) =>
        Ok(new ApiEnvelope<IReadOnlyCollection<CatalogFacility>>(
            await catalog.ListFacilitiesAsync(ct)));

    /// <summary>
    /// Every bookable court, narrowed by sport, by venue, or by neither.
    /// </summary>
    /// <param name="facility">
    /// One venue's id. By id rather than by name: a name can be edited and two
    /// venues can share one, and neither should change or widen what a filter
    /// matches.
    /// </param>
    [HttpGet("courts")]
    public async Task<IActionResult> Courts(
        [FromQuery] string? sport,
        [FromQuery] Guid? facility,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=60";

        return Ok(new ApiEnvelope<IReadOnlyCollection<CatalogCourt>>(
            await catalog.ListCourtsAsync(sport, facility, ct)));
    }

    /// <summary>
    /// One bookable court, whole: the court, the venue around it, and what it
    /// costs. The sport and division identify which offering is meant, because
    /// a court set up for three sports is three of them.
    /// </summary>
    [HttpGet("courts/{courtId:guid}")]
    public async Task<IActionResult> Court(
        Guid courtId,
        [FromQuery] string sport,
        [FromQuery] int division,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=60";

        var court = await catalog.GetCourtAsync(
            courtId,
            sport ?? string.Empty,
            division <= 0 ? 1 : division,
            ct);

        return court is null
            ? NotFound(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.NotFound,
                "That court is not on offer.")))
            : Ok(new ApiEnvelope<CatalogCourtDetail>(court));
    }
}
