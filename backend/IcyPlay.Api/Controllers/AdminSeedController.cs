using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// Builds a venue to demonstrate against.
///
/// Narrowed to named accounts on top of the admin role, and narrowed here on
/// the server rather than by hiding the button. This writes real rows into
/// whatever database it is pointed at: a check the browser keeps is a check
/// anybody can skip by calling the address directly.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/seed")]
public sealed class AdminSeedController(ISeedService seed) : ControllerBase
{
    /// <summary>
    /// Who may build one. Lower case, and compared lower case: an address typed
    /// with a capital is the same address.
    /// </summary>
    private static readonly string[] MayBuild =
    [
        "badicloud2011@gmail.com",
        "hr.icypay2022@gmail.com"
    ];

    /// <summary>
    /// Who may remove them — narrower, and deliberately one person.
    ///
    /// Building adds rows nobody was relying on. Removing takes away whatever
    /// anybody has done with them since, and there is no undo. The two are not
    /// the same risk and do not deserve the same list.
    /// </summary>
    private static readonly string[] MayRemove =
    [
        "badicloud2011@gmail.com"
    ];

    [HttpPost("venue")]
    public async Task<IActionResult> Venue(SeedVenueRequest? request, CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        if (!Is(MayBuild))
        {
            // Not found rather than forbidden: an address that answers
            // "forbidden" has told an unnamed admin that it is there.
            return NotFound(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.NotFound, "No such endpoint.")));
        }

        try
        {
            var result = await seed.BuildVenueAsync(actor, request?.OwnerEmail, ct);

            return Ok(new ApiEnvelope<SeedResult>(result));
        }
        catch (InvalidOperationException problem)
        {
            // The seeder refuses rather than half-builds, and says which step
            // it stopped at.
            return BadRequest(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.BadRequest, problem.Message)));
        }
    }

    /// <summary>
    /// What the seeder has standing, so the console can say what removing it
    /// would take with it before anybody presses the button.
    /// </summary>
    [HttpGet("venues")]
    public async Task<IActionResult> Venues(CancellationToken ct)
    {
        if (!Is(MayRemove))
        {
            return NotFound(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.NotFound, "No such endpoint.")));
        }

        var venues = await seed.SeededVenuesAsync(ct);

        return Ok(new ApiEnvelope<IReadOnlyCollection<SeededVenueSummary>>(venues));
    }

    /// <summary>
    /// Removes every seeded venue and everything under it.
    ///
    /// Scoped by the marker the seeder writes, so a real venue cannot be caught
    /// by it however it is named. There is no undo: the bookings made against
    /// these courts go with them, which is what demonstration data is for and
    /// is also why the console asks first.
    /// </summary>
    [HttpDelete("venues")]
    public async Task<IActionResult> RemoveVenues(CancellationToken ct)
    {
        if (CurrentActor() is not AuditActor actor)
        {
            return Unauthorized(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.Unauthorized, "Sign in again to continue.")));
        }

        if (!Is(MayRemove))
        {
            return NotFound(new ApiErrorEnvelope(
                new ApiError(ErrorCodes.NotFound, "No such endpoint.")));
        }

        var removed = await seed.RemoveSeededAsync(actor, ct);

        return Ok(new ApiEnvelope<SeedRemovalResult>(removed));
    }

    /// <summary>
    /// Whether the person holding this token is one of the named accounts. Read
    /// from the token rather than the database: the email is in it already, and
    /// this is a check about who is asking rather than about a record.
    /// </summary>
    [HttpGet("allowed")]
    public IActionResult Allowance() =>
        Ok(new ApiEnvelope<SeedAllowance>(new SeedAllowance(Is(MayBuild), Is(MayRemove))));

    private string? CurrentEmail() =>
        User.FindFirstValue(JwtRegisteredClaimNames.Email) ?? User.FindFirstValue(ClaimTypes.Email);

    private bool Is(string[] named) =>
        CurrentEmail() is string email
        && named.Contains(email.Trim(), StringComparer.OrdinalIgnoreCase);

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
}
