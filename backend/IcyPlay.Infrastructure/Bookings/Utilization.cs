using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// How much of what a venue had open actually got used.
///
/// Read in Entity Framework and added up here rather than in SQL, which is the
/// opposite of what a report usually wants. The reason is the denominator: "was
/// this court open, and for how long" is a rule that already exists, in
/// <see cref="BookingService"/>, and the availability grid answers it that way
/// for every customer. Restating it in T-SQL would be a second answer to the
/// same question, and the two would part company the first time somebody let a
/// court keep its own hours. A month of one venue is a few hundred rows; the
/// arithmetic is not what makes a report slow.
/// </summary>
internal static class Utilization
{
    public static async Task<UtilizationReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        IReadOnlyCollection<Guid> ownedVenueIds,
        UtilizationQuery query,
        CancellationToken ct)
    {
        // Every venue in scope, or none of it. Somebody who owns one building
        // and attends another would otherwise get a total that is the sum of
        // some of the courts below it, with no way of telling which.
        var everywhere = venueIds.All(ownedVenueIds.Contains);

        var courts = await db.Courts
            .AsNoTracking()
            .Where(court => court.IsActive && venueIds.Contains(court.FacilityId))
            .Include(court => court.OperatingHours)
            .Include(court => court.Facility).ThenInclude(facility => facility.OperatingHours)
            .Include(court => court.Facility).ThenInclude(facility => facility.FacilityOwner)
                .ThenInclude(owner => owner.Contracts)
            .Include(court => court.Sports).ThenInclude(pair => pair.BookableCourts)
            .Include(court => court.Sports).ThenInclude(pair => pair.Sport)
            .OrderBy(court => court.Facility.Name)
            .ThenBy(court => court.DisplayOrder)
            .ThenBy(court => court.Name)
            .ToListAsync(ct);

        if (courts.Count == 0)
        {
            return new UtilizationReport(
                query.From,
                query.To,
                0, 0, 0, 0, 0, 0,
                Money(everywhere, 0m),
                []);
        }

        var courtIds = courts.ConvertAll(court => court.Id);

        // The day the range ends, as an instant, so a maintenance period that starts at
        // half past eleven on the last night still counts against it.
        var opensOn = new DateTimeOffset(query.From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var closesOn = new DateTimeOffset(query.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var maintenance = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period =>
                period.LiftedAt == null
                && ((period.CourtId != null && courtIds.Contains(period.CourtId.Value))
                    || (period.CourtId == null && venueIds.Contains(period.FacilityId)))
                && period.StartsAt < closesOn
                && (period.EndsAt == null || period.EndsAt > opensOn))
            .Select(period => new Maintenance(
                period.CourtId,
                period.FacilityId,
                period.StartsAt,
                period.EndsAt))
            .ToListAsync(ct);

        // Confirmed is what was played. Waiting is counted on its own: it is
        // neither used nor lost, and folding it into either would make a report
        // run this afternoon disagree with the same report run tomorrow.
        var slots = await db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                courtIds.Contains(slot.CourtId)
                && slot.Date >= query.From
                && slot.Date <= query.To
                && (slot.Booking.Status == BookingStatus.Confirmed
                    || slot.Booking.Status == BookingStatus.PendingVerification))
            .Select(slot => new Sold(
                slot.CourtId,
                slot.BookableCourtId,
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.RateKind,
                slot.Amount,
                slot.Booking.Status == BookingStatus.Confirmed))
            .ToListAsync(ct);

        var byCourt = slots.ToLookup(slot => slot.CourtId);
        var reported = new List<CourtUtilization>(courts.Count);

        foreach (var court in courts)
        {
            var owned = ownedVenueIds.Contains(court.FacilityId);
            var mine = byCourt[court.Id].ToArray();
            var played = mine.Where(slot => slot.IsConfirmed).ToArray();

            // A court sport is live by existing; only the parts it is marked
            // out into are retired and brought back.
            //
            // A retired part is kept when it sold hours in the period. Dropping
            // it outright was quietly wrong: its hours stayed in the court's
            // SoldMinutes, so the rows no longer added up to the figure above
            // them and nothing on the page said what had gone.
            var units = court.Sports
                .SelectMany(pair => pair.BookableCourts
                    .Select(unit => new
                    {
                        Pair = pair,
                        Unit = unit,
                        Sold = played.Where(slot => slot.BookableCourtId == unit.Id).ToArray()
                    }))
                .Where(marked => marked.Unit.IsActive || marked.Sold.Length > 0)
                .OrderBy(marked => marked.Pair.Sport.Name)
                .ThenBy(marked => marked.Unit.DivisionNumber)
                .Select(marked => new UnitUtilization(
                    marked.Unit.Id,
                    DeskService.UnitLabel(
                        marked.Pair.Sport.Name,
                        marked.Unit.DivisionNumber,
                        marked.Pair.Divisions),
                    marked.Pair.Sport.Name,
                    marked.Pair.Sport.Key,
                    Minutes(marked.Sold),
                    Minutes(marked.Sold.Where(slot => slot.RateKind == CourtRateKind.Peak)),
                    !marked.Unit.IsActive,
                    Money(owned, marked.Sold.Sum(slot => slot.Amount))))
                .ToArray();

            var calendar = Days(court, maintenance, query);
            var inUse = InUseMinutes(played);

            reported.Add(new CourtUtilization(
                court.Id,
                court.FacilityId,
                court.Facility.Name,
                court.Name,
                calendar.OpenMinutes,
                inUse,
                Minutes(played),
                // Never below nothing. Hours sold before a court's timetable
                // was shortened can outlast the window that sold them, and a
                // court reading "−2h idle" would be read as a bug rather than
                // as the history it is.
                Math.Max(0, calendar.OpenMinutes - inUse),
                calendar.MaintenanceMinutes,
                InUseMinutes(mine.Where(slot => !slot.IsConfirmed)),
                calendar.OpenDays,
                calendar.MaintenanceDays,
                Money(owned, played.Sum(slot => slot.Amount)),
                units));
        }

        return new UtilizationReport(
            query.From,
            query.To,
            reported.Sum(court => court.OpenMinutes),
            reported.Sum(court => court.InUseMinutes),
            reported.Sum(court => court.IdleMinutes),
            reported.Sum(court => court.MaintenanceMinutes),
            reported.Sum(court => court.AwaitingMinutes),
            reported.Sum(court => court.OpenDays),
            Money(everywhere, reported.Sum(court => court.Rental ?? 0m)),
            reported);
    }

    /// <summary>
    /// Minutes the floor had somebody on it, counted once however many of its
    /// parts were sold for them.
    ///
    /// This is the whole reason the report has two numbers. A floor marked out
    /// three ways for pickleball can sell three o'clock three times over, and
    /// adding those up says the court was busy for three hours of an hour. The
    /// distinct window is what a person standing at the door would count.
    ///
    /// Distinct on the window rather than merged as intervals, because every
    /// slot on one court is cut from the same grid: they start at the same
    /// offsets and run the same length, so two that overlap at all are two that
    /// are identical. A court whose slot length changed mid-period would break
    /// that, and would also have re-cut its own diary.
    /// </summary>
    private static int InUseMinutes(IEnumerable<Sold> slots) =>
        slots
            .Select(slot => (slot.Date, slot.StartsAt, slot.EndsAt))
            .Distinct()
            .Sum(window => (int)(window.EndsAt - window.StartsAt).TotalMinutes);

    private static int Minutes(IEnumerable<Sold> slots) =>
        slots.Sum(slot => (int)(slot.EndsAt - slot.StartsAt).TotalMinutes);

    /// <summary>
    /// Null rather than zero for somebody who may not see it. An attendant's
    /// report has no money in it at all, because a figure the page hides is a
    /// figure anybody can read off the network tab.
    /// </summary>
    private static decimal? Money(bool maySee, decimal amount) => maySee ? amount : null;

    /// <summary>
    /// Walks the range a day at a time and adds up what each one was.
    ///
    /// A day is one of three things and never two: open for business, shut for
    /// work, or not trading at all — outside the opening hours, or outside the
    /// owner's contract. Keeping the second apart from the third is the whole
    /// point: a court closed for resurfacing has an explanation, and a venue
    /// looking at a bad month is owed it.
    /// </summary>
    private static Calendar Days(
        Court court,
        IReadOnlyCollection<Maintenance> periods,
        UtilizationQuery query)
    {
        var calendar = new Calendar(0, 0, 0, 0);

        for (var date = query.From; date <= query.To; date = date.AddDays(1))
        {
            var scheduled = ScheduledMinutes(court, date);

            if (scheduled == 0)
            {
                continue;
            }

            if (UnderMaintenance(court, date, periods))
            {
                calendar = calendar with
                {
                    MaintenanceMinutes = calendar.MaintenanceMinutes + scheduled,
                    MaintenanceDays = calendar.MaintenanceDays + 1
                };

                continue;
            }

            calendar = calendar with
            {
                OpenMinutes = calendar.OpenMinutes + scheduled,
                OpenDays = calendar.OpenDays + 1
            };
        }

        return calendar;
    }

    private static bool UnderMaintenance(
        Court court,
        DateOnly date,
        IReadOnlyCollection<Maintenance> periods)
    {
        var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        return periods.Any(period =>
            (period.CourtId == court.Id
                || (period.CourtId is null && period.FacilityId == court.FacilityId))
            && period.StartsAt < dayEnd
            && (period.EndsAt is null || dayStart < period.EndsAt));
    }

    /// <summary>
    /// What this court's timetable says it could sell on one date, before
    /// asking whether it was under maintenance.
    ///
    /// Rounded DOWN to whole slots, because the availability grid runs
    /// `while start + length &lt;= closesAt` and stops: a venue open thirteen
    /// hours on ninety-minute slots offers eight of them and keeps the last
    /// half hour to itself. Counting that half hour as open would put a ceiling
    /// on utilization that no court could ever reach, and every venue would
    /// read as slightly worse than it is.
    /// </summary>
    private static int ScheduledMinutes(Court court, DateOnly date)
    {
        // Outside a contract the court is not on sale at all, and that is not
        // maintenance either — there was no timetable to lose. Counting it
        // would show a venue as idle for a month it was not trading in.
        var trading = court.Facility.FacilityOwner.Contracts
            .Any(contract => contract.Covers(date));

        if (!trading)
        {
            return 0;
        }

        if (Hours(court, date.DayOfWeek) is not (TimeOnly opensAt, TimeOnly closesAt))
        {
            return 0;
        }

        var window = (int)(closesAt - opensAt).TotalMinutes;
        var length = court.SlotLengthMinutes;

        return window <= 0 || length <= 0 ? 0 : window / length * length;
    }

    /// <summary>
    /// The court's own hours when it has opted out, otherwise the building's.
    /// The same rule <see cref="BookingService"/> sells by; null on a day says
    /// it is shut.
    /// </summary>
    private static (TimeOnly, TimeOnly)? Hours(Court court, DayOfWeek day)
    {
        if (!court.UsesFacilityHours)
        {
            var own = court.OperatingHours.FirstOrDefault(hour => hour.DayOfWeek == day);

            return own is { OpensAt: TimeOnly opens, ClosesAt: TimeOnly closes }
                ? (opens, closes)
                : null;
        }

        var shared = court.Facility.OperatingHours.FirstOrDefault(hour => hour.DayOfWeek == day);

        return shared is { OpensAt: TimeOnly facilityOpens, ClosesAt: TimeOnly facilityCloses }
            ? (facilityOpens, facilityCloses)
            : null;
    }

    /// <summary>What the days in the range came to, once each was sorted.</summary>
    private sealed record Calendar(
        int OpenMinutes,
        int OpenDays,
        int MaintenanceMinutes,
        int MaintenanceDays);

    /// <summary>
    /// A maintenance period, flattened. Court-wide when <paramref name="CourtId"/> is set,
    /// building-wide when it is not.
    /// </summary>
    private sealed record Maintenance(
        Guid? CourtId,
        Guid FacilityId,
        DateTimeOffset StartsAt,
        DateTimeOffset? EndsAt);

    /// <summary>One booked slot, flattened to what the arithmetic needs.</summary>
    private sealed record Sold(
        Guid CourtId,
        Guid BookableCourtId,
        DateOnly Date,
        TimeOnly StartsAt,
        TimeOnly EndsAt,
        CourtRateKind RateKind,
        decimal Amount,
        bool IsConfirmed);
}
