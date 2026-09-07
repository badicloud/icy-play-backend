using IcyPlay.Api.Common;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(IAdminUserService adminUserService) : ControllerBase
{
    /// <summary>
    /// Platform-wide user list. Read-only by design: the console can look at
    /// accounts, and nothing on this controller changes one.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? role,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken ct = default)
    {
        var result = await adminUserService.ListAsync(
            new AdminUserQuery(search, role, page, pageSize, sortBy, sortDirection),
            ct);

        if (result is null)
        {
            return BadRequest(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.InvalidSortField,
                "Sort by email, fullName or createdAt.")));
        }

        return Ok(new ApiListEnvelope<AdminUserListItem>(
            result.Items,
            new PaginationMeta(result.Page, result.PageSize, result.TotalItems, result.TotalPages)));
    }
}
