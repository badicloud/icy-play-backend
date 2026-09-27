using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// How many payments the desk turned down, against how many it checked, and why.
///
/// "Checked" is refused plus confirmed, each on the day it was answered. A
/// refusal only happens to a payment that was waiting, and so does a
/// confirmation, so the two together are every answer the desk gave — which is
/// what a share of refusals has to be a share of.
/// </summary>
internal static class Declines
{
    public static async Task<DeclinesReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        HoursQuery query,
        bool maySeeMoney,
        CancellationToken ct)
    {
        // A day either side in UTC, so every venue's own day is inside what is
        // read whatever its offset; the exact cut is made on its clock below.
        var earliest = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var latest = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var mine = db.Bookings
            .AsNoTracking()
            .Where(booking => venueIds.Contains(booking.BookableCourt.Court.FacilityId));

        var refused = await mine
            .Where(booking => booking.Status == BookingStatus.Rejected
                && booking.CancelledAt >= earliest
                && booking.CancelledAt < latest)
            .Select(booking => new
            {
                booking.Id,
                DeclinedAt = booking.CancelledAt!.Value,
                booking.BookableCourt.Court.Facility.TimeZone,
                booking.FacilityName,
                booking.CourtName,
                booking.Kind,
                booking.StartDate,
                booking.EndDate,
                StartsAt = booking.Slots.Min(slot => (TimeOnly?)slot.StartsAt),
                EndsAt = booking.Slots.Max(slot => (TimeOnly?)slot.EndsAt),
                Hours = booking.Slots.Count,
                Amount = booking.Slots.Sum(slot => slot.Amount + slot.PlatformFee),
                booking.RejectionReason,
                booking.RejectionNote,
                booking.CancellationReason,
                booking.RejectedByUserId,
                OwnerUserId = booking.BookableCourt.Court.Facility.FacilityOwner.UserId,
                CustomerName = db.Users
                    .Where(user => user.Id == booking.CustomerUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault(),
                DeclinedByName = db.Users
                    .Where(user => user.Id == booking.RejectedByUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var confirmed = await mine
            .Where(booking => booking.ConfirmedAt >= earliest && booking.ConfirmedAt < latest)
            .Select(booking => new
            {
                ConfirmedAt = booking.ConfirmedAt!.Value,
                booking.BookableCourt.Court.Facility.TimeZone
            })
            .ToListAsync(ct);

        bool InRange(DateOnly day) => day >= query.From && day <= query.To;

        var declines = refused
            .Select(row => new DeclinedBooking(
                row.Id,
                row.DeclinedAt,
                DayOf(row.TimeZone, row.DeclinedAt),
                row.CustomerName ?? "Unknown customer",
                row.FacilityName,
                row.CourtName,
                row.Kind,
                row.StartDate,
                row.EndDate,
                row.StartsAt,
                row.EndsAt,
                row.Hours,
                maySeeMoney ? row.Amount : null,
                row.RejectionReason,
                // An old refusal has no note of its own: all the desk wrote is
                // in the sentence the customer reads.
                row.RejectionReason is null ? row.CancellationReason : row.RejectionNote,
                row.DeclinedByName,
                row.RejectedByUserId is Guid by && by == row.OwnerUserId))
            .Where(decline => InRange(decline.DeclinedOn))
            .OrderByDescending(decline => decline.DeclinedAt)
            .ToList();

        var confirmedOn = confirmed
            .Select(row => DayOf(row.TimeZone, row.ConfirmedAt))
            .Where(InRange)
            .ToList();

        var declinedBy = declines.ToLookup(decline => Utilization.Starts(decline.DeclinedOn, query.Grain));
        var confirmedBy = confirmedOn.ToLookup(day => Utilization.Starts(day, query.Grain));

        var periods = Utilization.PeriodsOf(query)
            .Select(period =>
            {
                var key = Utilization.Starts(period.Starts, query.Grain);
                var these = declinedBy[key].ToList();

                return new DeclinesPeriod(
                    period.Starts,
                    period.Ends,
                    these.Count,
                    these.Count + confirmedBy[key].Count(),
                    CountReasons(these, keepEmpty: true));
            })
            .ToList();

        return new DeclinesReport(
            query.From,
            query.To,
            query.Grain,
            periods,
            CountReasons(declines, keepEmpty: false),
            declines.Count,
            declines.Count + confirmedOn.Count,
            [.. declines.Take(DeclinesReport.Listed)]);
    }

    private static DateOnly DayOf(string? timeZone, DateTimeOffset moment) =>
        DateOnly.FromDateTime(VenueClock.LocalNowIn(timeZone, moment).DateTime);

    /// <summary>
    /// Each reason and how often it was given, then the uncategorised — every
    /// reason at zero for a period, so a chart's lines hold still; only the
    /// ones given, most first, for the range.
    /// </summary>
    private static List<ReasonCount> CountReasons(IReadOnlyCollection<DeclinedBooking> declines, bool keepEmpty)
    {
        var listed = RejectReason.All
            .Select(reason => new ReasonCount(reason, declines.Count(decline => decline.Reason == reason)));

        var counted = keepEmpty
            ? listed.ToList()
            : [.. listed.Where(count => count.Count > 0).OrderByDescending(count => count.Count)];

        var uncategorised = declines.Count(decline => decline.Reason is null);

        if (keepEmpty || uncategorised > 0)
        {
            counted.Add(new ReasonCount(null, uncategorised));
        }

        return counted;
    }
}
