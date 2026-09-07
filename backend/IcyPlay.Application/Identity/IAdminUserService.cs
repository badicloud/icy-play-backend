using IcyPlay.Application.Common;

namespace IcyPlay.Application.Identity;

public interface IAdminUserService
{
    /// <summary>
    /// Returns null when the requested sort field is not supported, so the API
    /// can answer INVALID_SORT_FIELD rather than quietly sorting by something
    /// the caller did not ask for.
    /// </summary>
    Task<PagedResult<AdminUserListItem>?> ListAsync(
        AdminUserQuery query,
        CancellationToken cancellationToken);
}
