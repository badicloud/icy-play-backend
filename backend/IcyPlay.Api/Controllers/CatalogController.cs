using IcyPlay.Api.Common;
using IcyPlay.Application.Facilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// How long a browser may hold the catalog before asking again.
///
/// It was a minute, which was chosen for the visitor: the listing changes
/// when an admin sets a court up, not between one person and the next, and a
/// minute of holding absorbs a burst of browsing for nothing.
///
/// It was the wrong number for the other reader of this endpoint. An owner
/// who changes a rate and opens the venue’s page to check it was shown the
/// rate they had just replaced, for up to a minute, with no way to tell that
/// from the save having failed — and an ordinary reload did not help, because
/// a reload honours this header. Only a hard refresh did, which is not
/// something anyone should have to know.
///
/// Five seconds keeps most of what the minute was for. A burst of clicks
/// through the catalog still comes off the browser, and the staleness is now
/// shorter than the walk from the pricing form to the page that shows it.
///
/// The real answer is an ETag, so the browser always asks and is told
/// "unchanged" for nothing. This is the cheap version of that.
/// </summary>
internal static class CatalogFreshness
{
    public const int Seconds = 5;
}

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
        Response.Headers.CacheControl = $"public, max-age={CatalogFreshness.Seconds}";

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
        Response.Headers.CacheControl = $"public, max-age={CatalogFreshness.Seconds}";

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
        Response.Headers.CacheControl = $"public, max-age={CatalogFreshness.Seconds}";

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
