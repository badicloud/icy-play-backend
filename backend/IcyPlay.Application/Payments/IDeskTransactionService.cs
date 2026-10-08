using IcyPlay.Application.Common;

namespace IcyPlay.Application.Payments;

/// <summary>
/// The online payments at the venues somebody works and may see the money of:
/// the list, the badge, and marking the list read.
/// </summary>
public interface IDeskTransactionService
{
    /// <summary>
    /// Newest first, with anything needing a person at the top. A venue the
    /// person does not work, or works without the money, answers as empty.
    /// </summary>
    Task<PagedResult<DeskTransaction>> ListAsync(
        Guid userId,
        Guid? facilityId,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<DeskTransactionSummary> SummaryAsync(Guid userId, CancellationToken ct);

    /// <summary>Everything that has arrived so far has been seen by this person.</summary>
    Task MarkSeenAsync(Guid userId, CancellationToken ct);
}
