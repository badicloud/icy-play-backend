using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// What customers paid the venue, on the day the money was accepted.
///
/// A booking's own payment is what it was paid when the desk confirmed it:
/// <see cref="Booking.PaidTotal"/> less the upgrades approved since, because
/// every upgrade adds its balance there and is counted on its own day. Worked
/// out that way rather than from the booking's hours as they stand, because a
/// free move to a cheaper court changes what the hours cost and not what was
/// paid for them — there are no refunds.
///
/// The platform fee is inside that payment, per hour booked, and a move keeps
/// the hours it had, so the slots' fees are still the fee that was paid.
/// </summary>
internal static class Takings
{
    public static async Task<TakingsReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        HoursQuery query,
        CancellationToken ct)
    {
        // A day either side in UTC, so every venue's own day is inside what is
        // read whatever its offset; the exact cut is made on its clock below.
        var earliest = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var latest = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var confirmed = await db.Bookings
            .AsNoTracking()
            .Where(booking => venueIds.Contains(booking.BookableCourt.Court.FacilityId)
                && booking.ConfirmedAt >= earliest
                && booking.ConfirmedAt < latest)
            .Select(booking => new
            {
                At = booking.ConfirmedAt!.Value,
                booking.BookableCourt.Court.Facility.TimeZone,
                booking.BookableCourt.CourtId,
                booking.BookableCourt.Court.FacilityId,
                FacilityName = booking.BookableCourt.Court.Facility.Name,
                CourtName = booking.BookableCourt.Court.Name,
                booking.PaidTotal,
                Hours = booking.Slots.Count,
                PlatformFee = booking.Slots.Sum(slot => slot.PlatformFee),
                UpgradesPaid = db.BookingUpgradeRequests
                    .Where(upgrade => upgrade.BookingId == booking.Id
                        && upgrade.Status == UpgradeStatus.Approved)
                    .Sum(upgrade => (decimal?)upgrade.BalanceDue) ?? 0m
            })
            .ToListAsync(ct);

        var upgraded = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Where(upgrade => upgrade.Status == UpgradeStatus.Approved
                && upgrade.BalanceDue > 0m
                && venueIds.Contains(upgrade.ToBookableCourt.Court.FacilityId)
                && upgrade.SettledAt >= earliest
                && upgrade.SettledAt < latest)
            .Select(upgrade => new
            {
                At = upgrade.SettledAt!.Value,
                upgrade.ToBookableCourt.Court.Facility.TimeZone,
                upgrade.ToBookableCourt.CourtId,
                upgrade.ToBookableCourt.Court.FacilityId,
                FacilityName = upgrade.ToBookableCourt.Court.Facility.Name,
                CourtName = upgrade.ToBookableCourt.Court.Name,
                upgrade.BalanceDue
            })
            .ToListAsync(ct);

        // One list of money, each entry on the venue's day it came in.
        var money = confirmed
            .Select(row => new Money(
                DayOf(row.TimeZone, row.At),
                new Court(row.CourtId, row.FacilityId, row.FacilityName, row.CourtName),
                Bookings: 1,
                row.Hours,
                Rental: row.PaidTotal - row.UpgradesPaid - row.PlatformFee,
                Upgrades: 0m,
                UpgradeCount: 0,
                row.PlatformFee))
            .Concat(upgraded.Select(row => new Money(
                DayOf(row.TimeZone, row.At),
                new Court(row.CourtId, row.FacilityId, row.FacilityName, row.CourtName),
                Bookings: 0,
                Hours: 0,
                Rental: 0m,
                Upgrades: row.BalanceDue,
                UpgradeCount: 1,
                PlatformFee: 0m)))
            .Where(entry => entry.On >= query.From && entry.On <= query.To)
            .ToList();

        var byPeriod = money.ToLookup(entry => Utilization.Starts(entry.On, query.Grain));
        var periods = Utilization.PeriodsOf(query);

        return new TakingsReport(
            query.From,
            query.To,
            query.Grain,
            [
                .. periods.Select(period =>
                {
                    var these = byPeriod[Utilization.Starts(period.Starts, query.Grain)];

                    return new TakingsPeriod(
                        period.Starts,
                        period.Ends,
                        these.Sum(entry => entry.Bookings),
                        these.Sum(entry => entry.Hours),
                        these.Sum(entry => entry.Rental),
                        these.Sum(entry => entry.Upgrades),
                        these.Sum(entry => entry.UpgradeCount),
                        these.Sum(entry => entry.PlatformFee));
                })
            ],
            [
                .. periods.SelectMany(period => byPeriod[Utilization.Starts(period.Starts, query.Grain)]
                    .GroupBy(entry => entry.Court)
                    .OrderBy(group => group.Key.FacilityName)
                    .ThenBy(group => group.Key.Name)
                    .Select(group => new CourtTakings(
                        period.Starts,
                        period.Ends,
                        group.Key.Id,
                        group.Key.FacilityId,
                        group.Key.FacilityName,
                        group.Key.Name,
                        group.Sum(entry => entry.Bookings),
                        group.Sum(entry => entry.Hours),
                        group.Sum(entry => entry.Rental),
                        group.Sum(entry => entry.Upgrades),
                        group.Sum(entry => entry.UpgradeCount),
                        group.Sum(entry => entry.PlatformFee))))
            ]);
    }

    private static DateOnly DayOf(string? timeZone, DateTimeOffset moment) =>
        DateOnly.FromDateTime(VenueClock.LocalNowIn(timeZone, moment).DateTime);

    private sealed record Court(Guid Id, Guid FacilityId, string FacilityName, string Name);

    private sealed record Money(
        DateOnly On,
        Court Court,
        int Bookings,
        int Hours,
        decimal Rental,
        decimal Upgrades,
        int UpgradeCount,
        decimal PlatformFee);
}
