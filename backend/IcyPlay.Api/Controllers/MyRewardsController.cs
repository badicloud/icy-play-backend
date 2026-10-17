using System.Security.Claims;
using IcyPlay.Api.Common;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// A player's rewards for being a best player at an open play: vouchers and
/// claim stubs. Signed-in customers only, and only their own.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.Customer)]
public sealed class MyRewardsController(IMyRewardService rewards) : ControllerBase
{
    [HttpGet("api/v1/my-rewards")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        return Ok(new ApiEnvelope<MyRewards>(await rewards.ListAsync(userId, ct)));
    }
}
