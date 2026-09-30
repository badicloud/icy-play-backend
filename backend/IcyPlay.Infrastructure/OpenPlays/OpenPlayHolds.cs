using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// The hours published open plays hold on one floor, for the dates asked about.
///
/// The one answer every "is this hour free" check asks for open plays: the
/// customer's availability and hold, a move, an upgrade the desk approves, and
/// an open play being published. Kept in one place so they cannot disagree.
///
/// Worked out from each series' rule rather than read from rows, because a
/// date only gets a session row once somebody registers or the desk cancels
/// it. A cancelled date is released. A draft holds nothing.
/// </summary>
internal static class OpenPlayHolds
{
    internal sealed record Hold(
        Guid OpenPlayId,
        string Title,
        DateOnly Date,
        TimeOnly StartsAt,
        TimeOnly EndsAt,
        Guid CourtSportId,
        int DivisionNumber);

    /// <param name="exceptOpenPlay">An open play checking against the others leaves itself out.</param>
    internal static async Task<IReadOnlyCollection<Hold>> OnCourtAsync(
        AppDbContext db,
        Guid courtId,
        IReadOnlyCollection<DateOnly> dates,
        CancellationToken ct,
        Guid? exceptOpenPlay = null)
    {
        if (dates.Count == 0)
        {
            return [];
        }

        var first = dates.Min();
        var last = dates.Max();

        var openPlays = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay =>
                openPlay.CourtId == courtId
                && openPlay.PublishedAt != null
                && openPlay.StartDate <= last
                && (openPlay.EndDate == null || openPlay.EndDate >= first)
                && (exceptOpenPlay == null || openPlay.Id != exceptOpenPlay))
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                openPlay.BookableCourt.CourtSportId,
                openPlay.BookableCourt.DivisionNumber
            })
            .ToListAsync(ct);

        if (openPlays.Count == 0)
        {
            return [];
        }

        var ids = openPlays.Select(row => row.OpenPlay.Id).ToArray();

        var cancelled = (await db.OpenPlaySessions
                .AsNoTracking()
                .Where(session => ids.Contains(session.OpenPlayId)
                    && session.CancelledAt != null
                    && session.Date >= first
                    && session.Date <= last)
                .Select(session => new { session.OpenPlayId, session.Date })
                .ToListAsync(ct))
            .Select(session => (session.OpenPlayId, session.Date))
            .ToHashSet();

        return
        [
            .. from row in openPlays
               from date in dates.Distinct()
               where row.OpenPlay.BlocksCourtOn(date) && !cancelled.Contains((row.OpenPlay.Id, date))
               select new Hold(
                   row.OpenPlay.Id,
                   row.OpenPlay.Title,
                   date,
                   row.OpenPlay.StartsAt,
                   row.OpenPlay.EndsAt,
                   row.CourtSportId,
                   row.DivisionNumber)
        ];
    }
}
