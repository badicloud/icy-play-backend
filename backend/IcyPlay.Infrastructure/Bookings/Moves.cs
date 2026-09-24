using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// How many bookings customers moved, and why.
///
/// Read from the moves table rather than the audit trail: the trail is a record
/// of what happened, written as sentences, and this is a count. The days are
/// the venue's, so a move at half past midnight in Manila counts on the day the
/// desk would say it happened.
/// </summary>
internal static class Moves
{
    public static async Task<MovesReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        HoursQuery query,
        CancellationToken ct)
    {
        // A day either side in UTC, so every venue's own day is inside what is
        // read whatever its offset; the exact cut is made on its clock below.
        var earliest = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var latest = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var rows = await db.BookingMoves
            .AsNoTracking()
            .Where(move => venueIds.Contains(move.Booking.BookableCourt.Court.FacilityId)
                && move.MovedAt >= earliest
                && move.MovedAt < latest)
            .Select(move => new
            {
                move.BookingId,
                move.MovedAt,
                move.Kind,
                move.Reason,
                move.ReasonNote,
                move.FromCourtName,
                move.ToCourtName,
                move.Booking.FacilityName,
                move.Booking.BookableCourt.Court.Facility.TimeZone,
                CustomerName = db.Users
                    .Where(user => user.Id == move.Booking.CustomerUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var moves = rows
            .Select(row => new MovedBooking(
                row.BookingId,
                row.MovedAt,
                DateOnly.FromDateTime(VenueClock.LocalNowIn(row.TimeZone, row.MovedAt).DateTime),
                row.CustomerName ?? "Unknown customer",
                row.FacilityName,
                row.FromCourtName,
                row.ToCourtName,
                row.Kind,
                row.Reason,
                row.ReasonNote))
            .Where(move => move.MovedOn >= query.From && move.MovedOn <= query.To)
            .OrderByDescending(move => move.MovedAt)
            .ToList();

        var byPeriod = moves.ToLookup(move => Utilization.Starts(move.MovedOn, query.Grain));

        var periods = Utilization.PeriodsOf(query)
            .Select(period =>
            {
                var these = byPeriod[Utilization.Starts(period.Starts, query.Grain)].ToList();

                return new MovesPeriod(
                    period.Starts,
                    period.Ends,
                    these.Count(move => move.Kind == MoveKind.Free),
                    these.Count(move => move.Kind == MoveKind.Upgrade),
                    CountReasons(these, keepEmpty: true));
            })
            .ToList();

        return new MovesReport(
            query.From,
            query.To,
            query.Grain,
            periods,
            CountReasons(moves, keepEmpty: false),
            moves.Count,
            [.. moves.Take(MovesReport.Listed)]);
    }

    /// <summary>
    /// Each reason on the list and how often it was given, then the unasked.
    ///
    /// For a period, every reason is there even at zero, so a chart's lines do
    /// not come and go. For the range, most given first, and a reason nobody
    /// gave is left out — "Weather: 0" in the summary says nothing.
    /// </summary>
    private static List<ReasonCount> CountReasons(IReadOnlyCollection<MovedBooking> moves, bool keepEmpty)
    {
        var listed = MoveReason.All
            .Select(reason => new ReasonCount(reason, moves.Count(move => move.Reason == reason)));

        var counted = keepEmpty
            ? listed.ToList()
            : [.. listed.Where(count => count.Count > 0).OrderByDescending(count => count.Count)];

        var unasked = moves.Count(move => move.Reason is null);

        if (keepEmpty || unasked > 0)
        {
            counted.Add(new ReasonCount(null, unasked));
        }

        return counted;
    }
}
