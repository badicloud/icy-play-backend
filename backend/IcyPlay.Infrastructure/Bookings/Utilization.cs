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

        var source = await LoadAsync(db, venueIds, query.From, query.To, ct);
        var courts = source.Courts;

        if (courts.Count == 0)
        {
            return new UtilizationReport(
                query.From,
                query.To,
                0, 0, 0, 0, 0,
                Money(everywhere, 0m),
                []);
        }

        var maintenance = source.Maintenance;
        var byCourt = source.Slots.ToLookup(slot => slot.CourtId);
        var reported = new List<CourtUtilization>(courts.Count);

        // The last sale on each court and each part, reaching back before the
        // range as far as it has to. A court that sold nothing this month
        // either had a quiet month or has sold nothing since March, and only
        // this tells the two apart. Confirmed only, the same as everywhere
        // else in the report: a hold that lapsed was never a sale.
        var courtIds = courts.ConvertAll(court => court.Id);

        var confirmed = db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                courtIds.Contains(slot.CourtId)
                && slot.Date <= query.To
                && slot.Booking.Status == BookingStatus.Confirmed);

        var lastOnCourt = await confirmed
            .GroupBy(slot => slot.CourtId)
            .Select(group => new { group.Key, Last = group.Max(slot => slot.Date) })
            .ToDictionaryAsync(row => row.Key, row => row.Last, ct);

        var lastOnPart = await confirmed
            .GroupBy(slot => slot.BookableCourtId)
            .Select(group => new { group.Key, Last = group.Max(slot => slot.Date) })
            .ToDictionaryAsync(row => row.Key, row => row.Last, ct);

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
                    lastOnPart.TryGetValue(marked.Unit.Id, out var partLast) ? partLast : null,
                    Money(owned, marked.Sold.Sum(slot => slot.Amount))))
                .ToArray();

            var calendar = Days(court, maintenance, query.From, query.To);
            var inUse = InUseMinutes(played);

            reported.Add(new CourtUtilization(
                court.Id,
                court.FacilityId,
                court.Facility.Name,
                court.Name,
                calendar.OpenMinutes,
                inUse,
                Minutes(played),
                calendar.MaintenanceMinutes,
                InUseMinutes(mine.Where(slot => !slot.IsConfirmed)),
                calendar.OpenDays,
                calendar.MaintenanceDays,
                lastOnCourt.TryGetValue(court.Id, out var courtLast) ? courtLast : null,
                Money(owned, played.Sum(slot => slot.Amount)),
                units));
        }

        return new UtilizationReport(
            query.From,
            query.To,
            reported.Sum(court => court.OpenMinutes),
            reported.Sum(court => court.InUseMinutes),
            reported.Sum(court => court.MaintenanceMinutes),
            reported.Sum(court => court.AwaitingMinutes),
            reported.Sum(court => court.OpenDays),
            Money(everywhere, reported.Sum(court => court.Rental ?? 0m)),
            reported);
    }

    /// <summary>
    /// The same three reads both of these reports need: the courts in scope,
    /// the closures touching the period, and the hours sold in it.
    ///
    /// Shared rather than written twice. The over-time read differs from the
    /// totals only in how it folds what comes back, and two copies of the
    /// query would drift on the day somebody narrowed one of them.
    /// </summary>
    private static async Task<Source> LoadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
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
            return new Source(courts, [], []);
        }

        var courtIds = courts.ConvertAll(court => court.Id);

        // The day the range ends, as an instant, so a maintenance period that
        // starts at half past eleven on the last night still counts against it.
        var opensOn = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var closesOn = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

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
                && slot.Date >= from
                && slot.Date <= to
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

        return new Source(courts, maintenance, slots);
    }

    /// <summary>
    /// The utilization figures cut by date rather than totalled per court.
    ///
    /// The same walk of the calendar and the same slots the totals use, folded
    /// into buckets instead of into one. That is the point: a venue reading the
    /// line beside the total must not find them disagreeing, and the only way
    /// to be sure of that is for there to be one sum.
    /// </summary>
    public static async Task<HoursOverTime> OverTimeAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        HoursQuery query,
        CancellationToken ct)
    {
        var source = await LoadAsync(db, venueIds, query.From, query.To, ct);

        var played = source.Slots
            .Where(slot => slot.IsConfirmed)
            .ToLookup(slot => (slot.CourtId, slot.Date));

        var rows = new List<CourtPeriod>();

        foreach (var court in source.Courts)
        {
            var buckets = new Dictionary<DateOnly, Bucket>();

            // Which parts sold in each bucket. A set rather than a count: two
            // bookings on Pickleball 1 in one week are still one part sold.
            var partsSold = new Dictionary<DateOnly, HashSet<Guid>>();

            foreach (var day in Walk(court, source.Maintenance, query.From, query.To))
            {
                var key = Starts(day.Date, query.Grain);
                var found = buckets.GetValueOrDefault(key, new Bucket(0, 0, 0));
                var today = played[(court.Id, day.Date)];

                buckets[key] = day.UnderMaintenance
                    ? found with { Maintenance = found.Maintenance + day.ScheduledMinutes }
                    : found with
                    {
                        Open = found.Open + day.ScheduledMinutes,
                        Sold = found.Sold + InUseMinutes(today)
                    };

                if (!partsSold.TryGetValue(key, out var sold))
                {
                    partsSold[key] = sold = [];
                }

                if (!day.UnderMaintenance)
                {
                    sold.UnionWith(today.Select(slot => slot.BookableCourtId));
                }
            }

            // The same parts the utilization report lists: the ones still
            // marked out, and a retired one only where it sold. Counted this
            // way the sold parts are always among the counted ones.
            var active = court.Sports
                .SelectMany(pair => pair.BookableCourts)
                .Where(unit => unit.IsActive)
                .Select(unit => unit.Id)
                .ToHashSet();

            foreach (var (key, bucket) in buckets.OrderBy(bucket => bucket.Key))
            {
                var period = Clamp(key, query);
                var sold = partsSold[key];

                rows.Add(new CourtPeriod(
                    period.Starts,
                    period.Ends,
                    court.Id,
                    court.FacilityId,
                    court.Facility.Name,
                    court.Name,
                    bucket.Open,
                    bucket.Sold,
                    bucket.Maintenance,
                    active.Count + sold.Count(id => !active.Contains(id)),
                    sold.Count));
            }
        }

        return new HoursOverTime(query.From, query.To, query.Grain, PeriodsOf(query), rows);
    }

    /// <summary>
    /// Every bucket in the range, in order. The moves report cuts its range
    /// here too, so a week means the same Monday-to-Sunday on every report.
    /// </summary>
    internal static List<ReportPeriod> PeriodsOf(HoursQuery query)
    {
        var periods = new List<ReportPeriod>();

        for (var key = Starts(query.From, query.Grain);
             key <= query.To;
             key = Ends(key, query.Grain).AddDays(1))
        {
            periods.Add(Clamp(key, query));
        }

        return periods;
    }

    /// <summary>
    /// A bucket as the range asked for it: the first and last say the days they
    /// actually cover, not the days the week or month they fall in would have.
    /// Rows and periods both come through here, so they cannot disagree about
    /// where a bucket starts.
    /// </summary>
    private static ReportPeriod Clamp(DateOnly key, HoursQuery query)
    {
        var ends = Ends(key, query.Grain);

        return new ReportPeriod(
            key < query.From ? query.From : key,
            ends > query.To ? query.To : ends);
    }

    /// <summary>
    /// Weeks start on a Monday, because a venue's week does. A Sunday-start
    /// week would cut most weekends in half and make every Saturday the busiest
    /// day of one bucket and the Sunday the quietest of the next.
    /// </summary>
    internal static DateOnly Starts(DateOnly date, string grain) => grain switch
    {
        HoursGrain.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        HoursGrain.Month => new DateOnly(date.Year, date.Month, 1),
        HoursGrain.Quarter => new DateOnly(date.Year, ((date.Month - 1) / 3 * 3) + 1, 1),
        HoursGrain.Half => new DateOnly(date.Year, date.Month <= 6 ? 1 : 7, 1),
        HoursGrain.Year => new DateOnly(date.Year, 1, 1),
        _ => date
    };

    private static DateOnly Ends(DateOnly starts, string grain) => grain switch
    {
        HoursGrain.Week => starts.AddDays(6),
        HoursGrain.Month => starts.AddMonths(1).AddDays(-1),
        HoursGrain.Quarter => starts.AddMonths(3).AddDays(-1),
        HoursGrain.Half => starts.AddMonths(6).AddDays(-1),
        HoursGrain.Year => starts.AddYears(1).AddDays(-1),
        _ => starts
    };

    private sealed record Source(
        List<Court> Courts,
        List<Maintenance> Maintenance,
        List<Sold> Slots);

    private sealed record Bucket(int Open, int Sold, int Maintenance);

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
        DateOnly from,
        DateOnly to)
    {
        var calendar = new Calendar(0, 0, 0, 0);

        foreach (var day in Walk(court, periods, from, to))
        {
            calendar = day.UnderMaintenance
                ? calendar with
                {
                    MaintenanceMinutes = calendar.MaintenanceMinutes + day.ScheduledMinutes,
                    MaintenanceDays = calendar.MaintenanceDays + 1
                }
                : calendar with
                {
                    OpenMinutes = calendar.OpenMinutes + day.ScheduledMinutes,
                    OpenDays = calendar.OpenDays + 1
                };
        }

        return calendar;
    }

    /// <summary>
    /// Each date this court could have traded on, and which of the two it was.
    ///
    /// The totals above and the over-time read below both fold this, rather
    /// than each counting the days for itself. Two walks would be two answers
    /// to "was this court open on the 10th", and the report that showed them
    /// side by side would be the thing that found out.
    ///
    /// Dates the court could not have traded at all — shut that weekday, or
    /// outside the owner's contract — are not yielded. They are not open and
    /// not maintenance either; there was no timetable to lose.
    /// </summary>
    private static IEnumerable<DayState> Walk(
        Court court,
        IReadOnlyCollection<Maintenance> periods,
        DateOnly from,
        DateOnly to)
    {
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var scheduled = ScheduledMinutes(court, date);

            if (scheduled == 0)
            {
                continue;
            }

            yield return new DayState(date, scheduled, UnderMaintenance(court, date, periods));
        }
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
        // would show a venue as unsold for a month it was not trading in.
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

    /// <summary>One date this court could have traded on, and what it was.</summary>
    private sealed record DayState(DateOnly Date, int ScheduledMinutes, bool UnderMaintenance);

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
