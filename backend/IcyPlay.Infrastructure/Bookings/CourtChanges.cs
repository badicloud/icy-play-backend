using System.Globalization;
using System.Text.Json;
using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// Every change made to a venue's courts, read back out of the audit trail.
///
/// The trail already has all of it — a court's details, sports, divisions,
/// prices, hours and photos are each written as the fields that changed, before
/// and after, and maintenance as an event. What it does not have is words: it
/// stores sport ids and "500.00/600.00/-/-". This turns each entry into the
/// sentence and the before-and-after lines a venue reads, on the venue's clock.
/// </summary>
internal static class CourtChanges
{
    private static readonly string[] Watched =
    [
        AuditAction.CourtCreated,
        AuditAction.CourtUpdated,
        AuditAction.CourtSportsUpdated,
        AuditAction.CourtDivisionsUpdated,
        AuditAction.CourtPricingUpdated,
        AuditAction.CourtHoursUpdated,
        AuditAction.CourtPhotosUpdated,
        AuditAction.MaintenanceSet,
        AuditAction.MaintenanceLifted
    ];

    public static async Task<CourtChangesReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        CourtChangesQuery query,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        // Every court these venues have had, retired ones too: a court taken
        // off sale in the range is one of the changes.
        var courts = await db.Courts
            .AsNoTracking()
            .Where(court => venueIds.Contains(court.FacilityId))
            .Select(court => new CourtRow(
                court.Id,
                court.FacilityId,
                court.Name,
                court.IsActive,
                court.Facility.TimeZone,
                court.Facility.Name))
            .ToDictionaryAsync(court => court.Id, ct);

        var zones = await db.Facilities
            .AsNoTracking()
            .Where(facility => venueIds.Contains(facility.Id))
            .ToDictionaryAsync(facility => facility.Id, facility => facility.TimeZone, ct);

        var sports = await db.Sports
            .AsNoTracking()
            .ToDictionaryAsync(sport => sport.Id.ToString(), sport => sport.Name, ct);

        var courtIds = query.CourtId is Guid one ? [one] : courts.Keys.ToList();

        // A day either side in UTC; the exact cut is made on each venue's clock.
        var earliest = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var latest = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var entries = await db.AuditLogs
            .AsNoTracking()
            .Where(entry => Watched.Contains(entry.Action)
                && entry.CreatedAt >= earliest
                && entry.CreatedAt < latest
                && ((entry.EntityType == AuditEntityType.Court && courtIds.Contains(entry.EntityId))
                    || (entry.EntityType == AuditEntityType.Facility && venueIds.Contains(entry.EntityId))))
            .OrderByDescending(entry => entry.CreatedAt)
            .Select(entry => new
            {
                entry.Id,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.OldValuesJson,
                entry.NewValuesJson,
                entry.Reason,
                entry.ActorRole,
                entry.CreatedAt,
                ActorName = db.Users
                    .Where(user => user.Id == entry.ActorUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var changes = new List<CourtChange>();

        foreach (var entry in entries)
        {
            var isVenue = entry.EntityType == AuditEntityType.Facility;
            var court = isVenue ? null : courts.GetValueOrDefault(entry.EntityId);

            // The venue's own entries are only its maintenance: a building-wide
            // closure closes every court in it.
            if (isVenue && entry.Action is not (AuditAction.MaintenanceSet or AuditAction.MaintenanceLifted))
            {
                continue;
            }

            var zone = isVenue ? zones.GetValueOrDefault(entry.EntityId) : court?.TimeZone;
            var local = VenueClock.LocalNowIn(zone, entry.CreatedAt);
            var on = DateOnly.FromDateTime(local.DateTime);

            if (on < query.From || on > query.To)
            {
                continue;
            }

            var context = new Entry(
                entry.Id,
                entry.CreatedAt,
                on,
                TimeOnly.FromDateTime(local.DateTime),
                isVenue ? null : entry.EntityId,
                isVenue ? "The whole venue" : court?.Name ?? "A court",
                Read(entry.OldValuesJson),
                Read(entry.NewValuesJson),
                string.IsNullOrWhiteSpace(entry.Reason) ? null : entry.Reason.Trim(),
                entry.ActorRole == UserRoleName.PlatformAdmin ? null : entry.ActorName,
                RoleLabel(entry.ActorRole),
                zone);

            changes.AddRange(Describe(entry.Action, context, sports));
        }

        var summary = await SummaryAsync(db, venueIds, query, courts, changes, utcNow, ct);

        return new CourtChangesReport(
            query.From,
            query.To,
            summary,
            [
                .. CourtChangeKind.All
                    .Select(kind => new ChangeKindCount(kind, changes.Count(change => change.Kind == kind)))
                    .Where(count => count.Count > 0)
            ],
            [.. changes.Take(CourtChangesReport.Listed)],
            changes.Count,
            [
                .. courts.Values
                    .OrderBy(court => court.FacilityName)
                    .ThenBy(court => court.Name)
                    .Select(court => new CourtOption(
                        court.Id,
                        court.FacilityId,
                        court.FacilityName,
                        court.Name,
                        court.IsActive))
            ]);
    }

    /// <summary>
    /// One audit entry as the changes a person would name. Usually one; a
    /// court's details saved together can be a rename and other details at
    /// once, and those read as two different things.
    /// </summary>
    private static IEnumerable<CourtChange> Describe(
        string action,
        Entry entry,
        IReadOnlyDictionary<string, string> sports)
    {
        switch (action)
        {
            case AuditAction.CourtCreated:
            {
                var name = entry.After.GetValueOrDefault("name") ?? entry.Subject;

                yield return entry.Change(
                    "",
                    CourtChangeKind.Added,
                    $"{name} added",
                    [
                        new ChangeDetail("Main sport", null, entry.After.GetValueOrDefault("primarySport")),
                        new ChangeDetail("Sports", null, entry.After.GetValueOrDefault("sports")),
                        new ChangeDetail("Venue type", null, entry.After.GetValueOrDefault("venueType"))
                    ]);
                break;
            }

            case AuditAction.CourtUpdated:
            {
                if (entry.Changed("name") is var (oldName, newName))
                {
                    yield return entry.Change(
                        "-name",
                        CourtChangeKind.RenamedOrRetired,
                        $"{oldName ?? entry.Subject} renamed",
                        [new ChangeDetail("Name", oldName, newName)]);
                }

                if (entry.Changed("isActive") is var (_, active))
                {
                    var retired = active == bool.FalseString;

                    yield return entry.Change(
                        "-active",
                        CourtChangeKind.RenamedOrRetired,
                        retired ? $"{entry.Subject} retired" : $"{entry.Subject} back on sale",
                        [
                            new ChangeDetail(
                                "On sale",
                                retired ? "Yes" : "No",
                                retired ? "No — past bookings on it are kept" : "Yes")
                        ]);
                }

                var details = DetailFields
                    .Where(field => entry.Changed(field.Key) is not null)
                    .Select(field =>
                    {
                        var (before, after) = entry.Changed(field.Key)!.Value;
                        return new ChangeDetail(field.Value, Pretty(field.Key, before), Pretty(field.Key, after));
                    })
                    .ToList();

                if (details.Count > 0)
                {
                    yield return entry.Change(
                        "-details",
                        CourtChangeKind.Details,
                        $"{entry.Subject} · details changed",
                        details);
                }

                break;
            }

            case AuditAction.CourtSportsUpdated:
            case AuditAction.CourtDivisionsUpdated:
            {
                var lines = new List<ChangeDetail>();

                if (entry.Changed("sportIds") is var (oldIds, newIds))
                {
                    var before = Ids(oldIds);
                    var after = Ids(newIds);

                    lines.AddRange(after.Except(before).Select(id => new ChangeDetail(Sport(sports, id), null, "Added")));
                    lines.AddRange(before.Except(after).Select(id => new ChangeDetail(Sport(sports, id), "Offered", null)));
                }

                if (entry.Changed("primarySportId") is var (oldMain, newMain))
                {
                    lines.Add(new ChangeDetail(
                        "Main sport",
                        oldMain is null ? null : Sport(sports, oldMain),
                        newMain is null ? null : Sport(sports, newMain)));
                }

                if (entry.Changed("divisions") is var (oldDivisions, newDivisions))
                {
                    var before = Divisions(oldDivisions);
                    var after = Divisions(newDivisions);

                    // Only sports on the court both before and after: one added
                    // or removed is already said above.
                    foreach (var (id, count) in after.Where(pair => before.ContainsKey(pair.Key)))
                    {
                        if (before[id] != count)
                        {
                            lines.Add(new ChangeDetail(Sport(sports, id), Courts(before[id]), Courts(count)));
                        }
                    }
                }

                if (lines.Count == 0)
                {
                    break;
                }

                var title = action == AuditAction.CourtDivisionsUpdated && lines.Count == 1
                    ? $"{entry.Subject} · {lines[0].Label} re-marked"
                    : $"{entry.Subject} · sports changed";

                yield return entry.Change("", CourtChangeKind.SportsAndDivisions, title, lines);
                break;
            }

            case AuditAction.CourtPricingUpdated:
            {
                var lines = new List<ChangeDetail>();
                var keys = entry.Before.Keys.Union(entry.After.Keys);

                foreach (var key in keys.Where(key => sports.ContainsKey(key)).OrderBy(key => Sport(sports, key)))
                {
                    var before = Rates(entry.Before.GetValueOrDefault(key));
                    var after = Rates(entry.After.GetValueOrDefault(key));

                    for (var index = 0; index < RateNames.Length; index++)
                    {
                        if (before[index] != after[index])
                        {
                            lines.Add(new ChangeDetail(
                                $"{Sport(sports, key)} {RateNames[index]}",
                                before[index],
                                after[index]));
                        }
                    }
                }

                if (entry.Changed("peakWindow") is var (oldWindow, newWindow))
                {
                    lines.Add(new ChangeDetail("Peak hours", Window(oldWindow), Window(newWindow)));
                }

                if (entry.Changed("peakDays") is var (oldDays, newDays))
                {
                    lines.Add(new ChangeDetail("Peak days", oldDays ?? "None", newDays ?? "None"));
                }

                if (lines.Count > 0)
                {
                    yield return entry.Change("", CourtChangeKind.Prices, $"{entry.Subject} · prices changed", lines);
                }

                break;
            }

            case AuditAction.CourtHoursUpdated:
            {
                var lines = new List<ChangeDetail>();

                if (entry.Changed("usesFacilityHours") is var (_, uses))
                {
                    lines.Add(uses == bool.TrueString
                        ? new ChangeDetail("Opening hours", "Its own", "The venue's")
                        : new ChangeDetail("Opening hours", "The venue's", "Its own"));
                }

                if (entry.Changed("hours") is var (oldHours, newHours))
                {
                    var before = Days(oldHours);
                    var after = Days(newHours);

                    foreach (var day in Enum.GetValues<DayOfWeek>())
                    {
                        var was = before.GetValueOrDefault(day.ToString());
                        var now = after.GetValueOrDefault(day.ToString());

                        if (was != now && (was is not null || now is not null))
                        {
                            lines.Add(new ChangeDetail(day.ToString(), was, now));
                        }
                    }
                }

                if (lines.Count > 0)
                {
                    yield return entry.Change("", CourtChangeKind.Hours, $"{entry.Subject} · opening hours changed", lines);
                }

                break;
            }

            case AuditAction.CourtPhotosUpdated:
            {
                var lines = new List<ChangeDetail>();

                if (entry.Changed("photos") is var (oldCount, newCount))
                {
                    lines.Add(new ChangeDetail("Photos", oldCount ?? "0", newCount ?? "0"));
                }

                if (entry.Changed("cover") is not null)
                {
                    lines.Add(new ChangeDetail("Cover photo", null, "Changed"));
                }

                if (lines.Count > 0)
                {
                    yield return entry.Change("", CourtChangeKind.Photos, $"{entry.Subject} · photos changed", lines);
                }

                break;
            }

            case AuditAction.MaintenanceSet:
            {
                var starts = Moment(entry.After.GetValueOrDefault("startsAt"), entry.TimeZone);
                var ends = entry.After.GetValueOrDefault("endsAt") is string written && written != "Until further notice"
                    ? Moment(written, entry.TimeZone)
                    : "until further notice";

                yield return entry.Change(
                    "",
                    CourtChangeKind.Maintenance,
                    $"{entry.Subject} closed for maintenance",
                    [new ChangeDetail("Closed", null, $"{starts} – {ends}")]);
                break;
            }

            case AuditAction.MaintenanceLifted:
                yield return entry.Change(
                    "",
                    CourtChangeKind.Maintenance,
                    $"{entry.Subject} reopened",
                    [new ChangeDetail("Maintenance", "On", "Lifted")]);
                break;
        }
    }

    /// <summary>
    /// The tiles: what the venue has now, and what the range added and took
    /// away. Counted from the tables for what exists and from the changes for
    /// what happened, so the two cannot be confused.
    /// </summary>
    private static async Task<CourtChangesSummary> SummaryAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        CourtChangesQuery query,
        IReadOnlyDictionary<Guid, CourtRow> courts,
        IReadOnlyCollection<CourtChange> changes,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var inScope = courts.Values
            .Where(court => query.CourtId is null || court.Id == query.CourtId)
            .Select(court => court.Id)
            .ToList();

        var earliest = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var latest = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var parts = await db.BookableCourts
            .AsNoTracking()
            .Where(unit => inScope.Contains(unit.CourtId))
            .Select(unit => new { unit.IsActive, unit.CreatedAt, unit.UpdatedAt, CourtActive = unit.Court.IsActive })
            .ToListAsync(ct);

        var closedNow = await db.MaintenancePeriods
            .AsNoTracking()
            .CountAsync(
                period => period.LiftedAt == null
                    && period.StartsAt <= utcNow
                    && (period.EndsAt == null || period.EndsAt > utcNow)
                    && (period.CourtId == null
                        ? venueIds.Contains(period.FacilityId)
                        : inScope.Contains(period.CourtId.Value)),
                ct);

        var prices = changes.Where(change => change.Kind == CourtChangeKind.Prices).ToList();

        return new CourtChangesSummary(
            courts.Values.Count(court => court.IsActive && inScope.Contains(court.Id)),
            changes.Count(change => change.Kind == CourtChangeKind.Added),
            changes.Count(change => change.Id.EndsWith("-active", StringComparison.Ordinal)
                && change.Title.EndsWith(" retired", StringComparison.Ordinal)),
            parts.Count(part => part.IsActive && part.CourtActive),
            parts.Count(part => part.CreatedAt >= earliest && part.CreatedAt < latest),
            parts.Count(part => !part.IsActive && part.UpdatedAt >= earliest && part.UpdatedAt < latest),
            prices.Count,
            prices.Select(change => change.CourtId).Distinct().Count(),
            changes.Count(change => change.Kind == CourtChangeKind.Maintenance
                && change.Title.EndsWith("closed for maintenance", StringComparison.Ordinal)),
            closedNow);
    }

    /// <summary>A court's detail fields a venue would recognise, and what to call them.</summary>
    private static readonly IReadOnlyDictionary<string, string> DetailFields = new Dictionary<string, string>
    {
        ["description"] = "Description",
        ["venueType"] = "Venue type",
        ["surface"] = "Surface",
        ["hasLighting"] = "Lighting",
        ["sizeLabel"] = "Size",
        ["capacity"] = "Capacity",
        ["equipment"] = "Equipment",
        ["slotLengthMinutes"] = "Slot length",
        ["minimumDurationMinutes"] = "Shortest booking",
        ["bufferMinutes"] = "Gap between bookings",
        ["displayOrder"] = "Listing order"
    };

    private static readonly string[] RateNames = ["standard", "peak", "weekend", "holiday"];

    private static string? Pretty(string field, string? value) => (field, value) switch
    {
        (_, null or "") => null,
        ("hasLighting", _) => value == bool.TrueString ? "Yes" : "No",
        ("slotLengthMinutes" or "minimumDurationMinutes" or "bufferMinutes", _) => $"{value} min",
        _ => value
    };

    private static string RoleLabel(string role) => role switch
    {
        UserRoleName.FacilityOwner => "Owner",
        UserRoleName.FacilityAttendant => "Attendant",
        UserRoleName.PlatformAdmin => "Platform admin",
        _ => role
    };

    private static string Sport(IReadOnlyDictionary<string, string> sports, string id) =>
        sports.GetValueOrDefault(id) ?? "A sport";

    private static string Courts(int count) => count == 1 ? "1 court" : $"{count} courts";

    private static HashSet<string> Ids(string? joined) =>
        [.. (joined ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>"sportId:3,sportId:1" as sport → divisions.</summary>
    private static Dictionary<string, int> Divisions(string? joined) =>
        (joined ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(':'))
            .Where(parts => parts.Length == 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out _))
            .ToDictionary(parts => parts[0], parts => int.Parse(parts[1], CultureInfo.InvariantCulture));

    /// <summary>"500.00/600.00/-/-" as four pesos, "Not set" for a dash, all four "Not priced" for no rates.</summary>
    private static string[] Rates(string? joined)
    {
        if (joined is null)
        {
            return ["Not priced", "Not priced", "Not priced", "Not priced"];
        }

        var parts = joined.Split('/');

        return
        [
            .. Enumerable.Range(0, RateNames.Length).Select(index =>
                index < parts.Length
                && decimal.TryParse(parts[index], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                    ? $"₱{amount.ToString("#,##0.##", CultureInfo.InvariantCulture)}"
                    : "Not set")
        ];
    }

    /// <summary>"17:00-20:00" as "5:00 PM – 8:00 PM".</summary>
    private static string Window(string? window)
    {
        if (window is null)
        {
            return "None";
        }

        var parts = window.Split('-');

        return parts.Length == 2 ? $"{Clock(parts[0])} – {Clock(parts[1])}" : window;
    }

    /// <summary>"Monday:06:00-22:00,Sunday:closed" as day → "6:00 AM – 10:00 PM" or "Closed".</summary>
    private static Dictionary<string, string> Days(string? joined) =>
        (joined ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => (Day: pair[..Math.Max(0, pair.IndexOf(':'))], Hours: pair[(pair.IndexOf(':') + 1)..]))
            .Where(pair => pair.Day.Length > 0)
            .ToDictionary(
                pair => pair.Day,
                pair => pair.Hours == "closed" ? "Closed" : Window(pair.Hours));

    private static string Clock(string time) =>
        TimeOnly.TryParse(time, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : time;

    /// <summary>A stored UTC moment as the venue's day and time: "24 Sep, 8:00 AM".</summary>
    private static string Moment(string? written, string? zone) =>
        DateTimeOffset.TryParse(written, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment)
            ? VenueClock.LocalNowIn(zone, moment).ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture)
            : written ?? "now";

    private static Dictionary<string, string?> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
        }
        catch (JsonException)
        {
            // An entry that cannot be read still happened; it just has no lines.
            return [];
        }
    }

    private sealed record CourtRow(
        Guid Id,
        Guid FacilityId,
        string Name,
        bool IsActive,
        string TimeZone,
        string FacilityName);

    /// <summary>One audit entry, unpacked, with what every change made from it shares.</summary>
    private sealed record Entry(
        Guid Id,
        DateTimeOffset At,
        DateOnly On,
        TimeOnly Time,
        Guid? CourtId,
        string Subject,
        Dictionary<string, string?> Before,
        Dictionary<string, string?> After,
        string? Reason,
        string? ActorName,
        string ActorRole,
        string? TimeZone)
    {
        /// <summary>Before and after for a field that changed; null when it did not.</summary>
        public (string? Before, string? After)? Changed(string field) =>
            After.ContainsKey(field) || Before.ContainsKey(field)
                ? Before.GetValueOrDefault(field) == After.GetValueOrDefault(field)
                    ? null
                    : (Before.GetValueOrDefault(field), After.GetValueOrDefault(field))
                : null;

        public CourtChange Change(string suffix, string kind, string title, IReadOnlyCollection<ChangeDetail> details) =>
            new(
                $"{Id}{suffix}",
                At,
                On,
                Time,
                kind,
                CourtId,
                title,
                [.. details.Where(detail => detail.Before is not null || detail.After is not null)],
                Reason,
                ActorName,
                ActorRole);
    }
}
