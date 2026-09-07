namespace IcyPlay.Api.Common;

/// <summary>
/// The list response shape from docs/api-design.md: data alongside pagination,
/// not nested inside meta.
/// </summary>
public sealed record ApiListEnvelope<T>(
    IReadOnlyCollection<T> Data,
    PaginationMeta Pagination);

public sealed record PaginationMeta(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);
