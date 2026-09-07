using IcyPlay.Application.Common;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Identity;

public sealed class AdminUserService(AppDbContext db) : IAdminUserService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    /// <summary>
    /// A whitelist, not a passthrough: an arbitrary sort string reaching the
    /// query would be both a correctness and an injection problem.
    /// </summary>
    private static readonly Dictionary<string, Func<IQueryable<User>, bool, IQueryable<User>>> Sorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["email"] = (query, descending) =>
                descending ? query.OrderByDescending(x => x.Email) : query.OrderBy(x => x.Email),
            ["fullName"] = (query, descending) =>
                descending ? query.OrderByDescending(x => x.FullName) : query.OrderBy(x => x.FullName),
            ["createdAt"] = (query, descending) =>
                descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
        };

    public async Task<PagedResult<AdminUserListItem>?> ListAsync(
        AdminUserQuery query,
        CancellationToken ct)
    {
        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "createdAt" : query.SortBy.Trim();
        if (!Sorts.TryGetValue(sortBy, out var applySort))
        {
            return null;
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize,
            1,
            MaximumPageSize);
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase) ||
            (query.SortDirection is null && sortBy.Equals("createdAt", StringComparison.OrdinalIgnoreCase));

        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(user =>
                EF.Functions.Like(user.Email, $"%{search}%") ||
                EF.Functions.Like(user.FullName, $"%{search}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();
            users = users.Where(user => user.Roles.Any(assignment => assignment.Role == role));
        }

        var totalItems = await users.CountAsync(ct);

        var items = await applySort(users, descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            // IsEmailVerified is a computed property and cannot be translated, so
            // the column it derives from is projected instead.
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.FullName,
                user.PhoneNumber,
                user.IsActive,
                user.EmailVerifiedAt,
                user.LockoutEnd,
                user.CreatedAt,
                Roles = user.Roles.Select(assignment => assignment.Role).ToList()
            })
            .ToListAsync(ct);

        return new PagedResult<AdminUserListItem>(
            items
                .Select(user => new AdminUserListItem(
                    user.Id,
                    user.Email,
                    user.FullName,
                    user.PhoneNumber,
                    user.Roles,
                    user.IsActive,
                    user.EmailVerifiedAt is not null,
                    user.EmailVerifiedAt,
                    user.LockoutEnd,
                    user.CreatedAt))
                .ToArray(),
            page,
            pageSize,
            totalItems);
    }
}
