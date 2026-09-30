using IcyPlay.Api.Common;
using IcyPlay.Application.OpenPlays;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// The open plays the public site can offer. Anonymous, like the court
/// catalogue: a visitor should see what is on before being asked to sign in.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/open-plays")]
public sealed class OpenPlaysController(IOpenPlayCatalog catalog) : ControllerBase
{
    /// <summary>
    /// Every open play with a date still to come, narrowed by sport, by venue,
    /// or by neither. Each comes with its next few dates, spots left, and what
    /// a player registering now would pay.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? sport,
        [FromQuery] Guid? facility,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = $"public, max-age={CatalogFreshness.Seconds}";

        return Ok(new ApiEnvelope<IReadOnlyCollection<CatalogOpenPlay>>(
            await catalog.ListAsync(sport, facility, ct)));
    }
}
