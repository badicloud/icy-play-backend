using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// What a venue's open, unsold hours would have earned, at its own rates.
///
/// Built on the utilization report's own walk of the calendar — the same open
/// days, the same maintenance, the same contract — and priced by the same rule
/// the booking page sells by (<see cref="CourtSport.PriceAt"/>), so an empty
/// peak hour here is worth exactly what a customer would have been charged.
///
/// Two rules decide what counts:
///
///  - **Only hours that have begun.** One still ahead can still be sold, so it
///    is not missed yet. The venue's clock decides, not the server's.
///  - **Only what could have been sold.** A sport court is missed in an hour
///    only when nothing clashing held the floor: basketball across the hall
///    takes the pickleball courts with it, and those were not for sale.
///
/// A court's own money is its main sport — every part of it still sellable in
/// the hour. All of them when the floor was empty, the rest when some were
/// booked, and none when another sport had it.
/// </summary>
internal static class Missed
{
    public static async Task<MissedReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        HoursQuery query,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var source = await Utilization.LoadAsync(db, venueIds, query.From, query.To, ct);

        // Read once and asked per date: repeating holidays cannot be matched on
        // their stored date in SQL, the same as the booking page does it.
        var holidays = await db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.IsActive)
            .ToArrayAsync(ct);

        var booked = source.Slots.ToLookup(slot => (slot.CourtId, slot.Date));
        var courts = new List<CourtMissed>();
        var rows = new List<CourtMissedPeriod>();
        var venue = new Dictionary<DateOnly, Tally>();

        foreach (var court in source.Courts)
        {
            var venueNow = VenueClock.LocalNowIn(court.Facility.TimeZone, utcNow);
            var today = DateOnly.FromDateTime(venueNow.DateTime);
            var now = TimeOnly.FromDateTime(venueNow.DateTime);

            // Every part the floor has ever been sold in, so a booking on a
            // retired one still blocks what it clashes with. Only the live ones
            // are priced as missed.
            var everyPart = court.Sports
                .SelectMany(pair => pair.BookableCourts)
                .ToDictionary(unit => unit.Id);

            var main = court.Sports.FirstOrDefault(pair => pair.IsPrimary) ?? court.Sports.FirstOrDefault();

            var parts = court.Sports
                .SelectMany(pair => pair.BookableCourts
                    .Where(unit => unit.IsActive)
                    .Select(unit => new Part(pair, unit, new Tally())))
                .ToList();

            var whole = new Tally();
            var byPeriod = new Dictionary<DateOnly, Tally>();
            var length = court.SlotLengthMinutes;

            foreach (var day in Utilization.Walk(court, source.Maintenance, query.From, query.To))
            {
                if (day.UnderMaintenance || day.Date > today || length <= 0)
                {
                    continue;
                }

                if (Utilization.Hours(court, day.Date.DayOfWeek) is not (TimeOnly opensAt, TimeOnly closesAt))
                {
                    continue;
                }

                var isHoliday = holidays.Any(holiday => holiday.Covers(day.Date));
                var taken = booked[(court.Id, day.Date)].ToList();
                var key = Utilization.Starts(day.Date, query.Grain);
                var period = byPeriod.TryGetValue(key, out var found) ? found : byPeriod[key] = new Tally();
                var step = TimeSpan.FromMinutes(length);

                for (var start = opensAt; start.Add(step) <= closesAt; start = start.Add(step))
                {
                    // Not begun, so not missed: it is still on sale.
                    if (day.Date == today && start > now)
                    {
                        break;
                    }

                    var end = start.Add(step);
                    var held = taken.Where(slot => slot.StartsAt < end && start < slot.EndsAt).ToList();
                    var peak = court.IsPeakAt(day.Date.DayOfWeek, start);

                    whole.Open += length;
                    period.Open += length;

                    if (held.Count == 0)
                    {
                        whole.NotSold += length;
                        period.NotSold += length;

                        if (peak)
                        {
                            whole.PeakNotSold += length;
                            period.PeakNotSold += length;
                        }
                    }

                    foreach (var part in parts)
                    {
                        var blocked = held.Any(slot =>
                            !everyPart.TryGetValue(slot.BookableCourtId, out var holding)
                            || part.Unit.ClashesWith(holding.CourtSportId, holding.DivisionNumber));

                        if (blocked || part.Pair.PriceAt(court, day.Date, start, isHoliday) is not SlotRate price)
                        {
                            continue;
                        }

                        var amount = BookingService.PerSlot(price.Amount, length);

                        part.Tally.NotSold += length;
                        part.Tally.Missed += amount;

                        if (peak)
                        {
                            part.Tally.PeakNotSold += length;
                        }

                        if (part.Pair == main)
                        {
                            whole.Missed += amount;
                            period.Missed += amount;

                            if (peak)
                            {
                                whole.PeakMissed += amount;
                                period.PeakMissed += amount;
                            }
                        }
                    }
                }
            }

            foreach (var (key, tally) in byPeriod.OrderBy(entry => entry.Key))
            {
                var clamped = Utilization.PeriodsOf(query).First(one => Utilization.Starts(one.Starts, query.Grain) == key);

                rows.Add(new CourtMissedPeriod(
                    clamped.Starts,
                    clamped.Ends,
                    court.Id,
                    court.FacilityId,
                    court.Facility.Name,
                    court.Name,
                    tally.Open,
                    tally.NotSold,
                    tally.Missed));

                var sum = venue.TryGetValue(key, out var already) ? already : venue[key] = new Tally();
                sum.Add(tally);
            }

            courts.Add(new CourtMissed(
                court.Id,
                court.FacilityId,
                court.Facility.Name,
                court.Name,
                main?.Sport.Name ?? string.Empty,
                whole.Open,
                whole.NotSold,
                whole.PeakNotSold,
                whole.Missed,
                whole.PeakMissed,
                [
                    .. parts
                        .OrderByDescending(part => part.Pair == main)
                        .ThenBy(part => part.Pair.Sport.Name)
                        .ThenBy(part => part.Unit.DivisionNumber)
                        .Select(part => new UnitMissed(
                            part.Unit.Id,
                            DeskService.UnitLabel(part.Pair.Sport.Name, part.Unit.DivisionNumber, part.Pair.Divisions),
                            part.Pair.Sport.Name,
                            part.Pair == main,
                            part.Tally.NotSold,
                            part.Tally.PeakNotSold,
                            part.Tally.Missed))
                ]));
        }

        return new MissedReport(
            query.From,
            query.To,
            query.Grain,
            [
                .. Utilization.PeriodsOf(query).Select(period =>
                {
                    var tally = venue.GetValueOrDefault(Utilization.Starts(period.Starts, query.Grain)) ?? new Tally();

                    return new MissedPeriod(
                        period.Starts,
                        period.Ends,
                        tally.Open,
                        tally.NotSold,
                        tally.PeakNotSold,
                        tally.Missed,
                        tally.PeakMissed);
                })
            ],
            rows,
            courts);
    }

    private sealed record Part(CourtSport Pair, BookableCourt Unit, Tally Tally);

    /// <summary>Minutes and money, added up as the calendar is walked.</summary>
    private sealed class Tally
    {
        public int Open { get; set; }
        public int NotSold { get; set; }
        public int PeakNotSold { get; set; }
        public decimal Missed { get; set; }
        public decimal PeakMissed { get; set; }

        public void Add(Tally other)
        {
            Open += other.Open;
            NotSold += other.NotSold;
            PeakNotSold += other.PeakNotSold;
            Missed += other.Missed;
            PeakMissed += other.PeakMissed;
        }
    }
}
