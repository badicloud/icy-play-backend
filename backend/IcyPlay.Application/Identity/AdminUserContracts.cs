namespace IcyPlay.Application.Identity;

/// <summary>
/// Filters for the platform-wide user list. Read-only: the admin console can
/// look at accounts, and nothing here changes one.
/// </summary>
public sealed record AdminUserQuery(
    string? Search = null,
    string? Role = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    string? SortDirection = null);

public sealed record AdminUserListItem(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    IReadOnlyCollection<string> Roles,
    bool IsActive,
    bool IsEmailVerified,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset? LockoutEnd,
    DateTimeOffset CreatedAt);
