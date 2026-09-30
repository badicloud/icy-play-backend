using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Puts sample open plays on the demonstration venues.
///
/// Seeded venues only. An open play blocks its court hours, and a sample one
/// on a real venue would turn real customers away from a court nobody is
/// actually running a session on.
/// </summary>
public sealed class OpenPlaySeedService(
    AppDbContext db,
    IAuditLogger audit,
    TimeProvider timeProvider) : IOpenPlaySeedService
{
    private const int Minutes = 1;
    private const int Hours = 60 * Minutes;
    private const int Days = 24 * Hours;

    /// <summary>
    /// What gets built on each seeded venue. The venue seeder opens six to ten
    /// and shuts on Sundays, so every sample fits inside those hours.
    /// Different sports, levels, weekdays, and both kinds of early bird, so each
    /// part of the feature has something to show.
    /// </summary>
    private static readonly Sample[] Samples =
    [
        new("Saturday Night Dinkers", "pickleball", "Court 1", 1, OpenPlayLevel.AllLevels,
            16, 150m, OpenPlayDays.Saturday, new(18, 0), new(21, 0), 1 * Hours,
            new OpenPlayEarlyBird(OpenPlayDiscountKind.Fixed, 30m, 3 * Days), null),
        new("Beginner Pickleball Mornings", "pickleball", "Court 1", 2, OpenPlayLevel.Beginner,
            12, 120m, OpenPlayDays.Tuesday | OpenPlayDays.Thursday, new(7, 0), new(9, 0), 2 * Hours,
            null, null),
        new("Advanced Pickleball Ladder", "pickleball", "Court 2", 1, OpenPlayLevel.Advanced,
            8, 200m, OpenPlayDays.Wednesday, new(17, 0), new(20, 0), 30 * Minutes,
            new OpenPlayEarlyBird(OpenPlayDiscountKind.Percentage, 10m, 2 * Days), 8),
        new("Badminton Open Play", "badminton", "Court 3", 1, OpenPlayLevel.Intermediate,
            10, 180m, OpenPlayDays.Monday | OpenPlayDays.Wednesday | OpenPlayDays.Friday,
            new(19, 0), new(22, 0), 1 * Hours,
            new OpenPlayEarlyBird(OpenPlayDiscountKind.Percentage, 15m, 1 * Days), null),
        new("Weekend Pickup Basketball", "basketball", "Court 4", 1, OpenPlayLevel.AllLevels,
            20, 100m, OpenPlayDays.Saturday, new(8, 0), new(11, 0), 1 * Hours,
            new OpenPlayEarlyBird(OpenPlayDiscountKind.Fixed, 20m, 2 * Days), null)
    ];

    public async Task<OpenPlaySeedResult> BuildAsync(AuditActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var createdBy = actor.UserId
            ?? throw new InvalidOperationException("Sample open plays can only be built by a signed-in admin.");

        var facilities = await db.Facilities
            .AsNoTracking()
            .Where(facility => facility.FacilityOwner.SeededAt != null)
            .Select(facility => new { facility.Id, facility.Name, facility.TimeZone })
            .ToListAsync(ct);

        if (facilities.Count == 0)
        {
            throw new InvalidOperationException(
                "There is no demo venue to put them on. Build a demo venue first.");
        }

        var now = timeProvider.GetUtcNow();
        var built = new List<(OpenPlay OpenPlay, string Facility, string Court, string Sport)>();
        var skipped = new List<string>();

        foreach (var facility in facilities)
        {
            var today = DateOnly.FromDateTime(VenueClock.LocalNowIn(facility.TimeZone, now).DateTime);

            var bookables = await db.BookableCourts
                .AsNoTracking()
                .Where(bookable => bookable.IsActive && bookable.Court.FacilityId == facility.Id)
                .Select(bookable => new Bookable(
                    bookable.Id,
                    bookable.CourtId,
                    bookable.CourtSportId,
                    bookable.DivisionNumber,
                    bookable.Court.Name,
                    bookable.CourtSport.Sport.Key,
                    bookable.CourtSport.Sport.Name))
                .ToListAsync(ct);

            var courtIds = bookables.Select(bookable => bookable.CourtId).Distinct().ToArray();

            // Every live hour from today on, across the venue. Read once rather
            // than per sample, because each sample asks the same question of it.
            var booked = await db.BookingSlots
                .AsNoTracking()
                .Where(slot => courtIds.Contains(slot.CourtId)
                    && slot.Date >= today
                    && BookingStatuses.Live.Contains(slot.Booking.Status))
                .Select(slot => new Held(
                    slot.CourtId,
                    slot.BookableCourt.CourtSportId,
                    slot.BookableCourt.DivisionNumber,
                    slot.Date,
                    slot.StartsAt,
                    slot.EndsAt))
                .ToListAsync(ct);

            // With the part of the floor each is on beside it, because the ones
            // built in this run have no bookable court loaded to read it from.
            var running = (await db.OpenPlays
                    // A draft holds nothing, so only the published ones are in the way.
                    .Where(openPlay => openPlay.FacilityId == facility.Id && openPlay.PublishedAt != null)
                    .Include(openPlay => openPlay.BookableCourt)
                    .ToListAsync(ct))
                .Select(openPlay => (
                    OpenPlay: openPlay,
                    openPlay.BookableCourt.CourtSportId,
                    openPlay.BookableCourt.DivisionNumber))
                .ToList();

            foreach (var sample in Samples)
            {
                var where = $"{sample.Title} at {facility.Name}";

                var bookable = bookables.FirstOrDefault(candidate =>
                    candidate.CourtName == sample.CourtName
                    && candidate.SportKey == sample.SportKey
                    && candidate.DivisionNumber == sample.DivisionNumber);

                if (bookable is null)
                {
                    skipped.Add($"{where}: {sample.CourtName} is not set up for {sample.SportKey}.");
                    continue;
                }

                var openPlay = new OpenPlay(
                    facility.Id,
                    bookable.Id,
                    bookable.CourtId,
                    sample.Title,
                    sample.Level,
                    sample.MaxPlayers,
                    sample.Fee,
                    sample.StartsAt,
                    sample.EndsAt,
                    sample.Days,
                    today,
                    sample.Weeks is int weeks ? today.AddDays(7 * weeks) : null,
                    sample.CutoffMinutes,
                    // A fresh copy each time: EF owns it as part of the row,
                    // and one instance cannot belong to two rows.
                    sample.EarlyBird is { } earlyBird
                        ? new OpenPlayEarlyBird(earlyBird.DiscountKind, earlyBird.DiscountValue, earlyBird.LeadMinutes)
                        : null,
                    createdBy,
                    now);

                var clashesWithAnOpenPlay = running.Any(other =>
                    other.OpenPlay.CourtId == bookable.CourtId
                    && Clashes(bookable, other.CourtSportId, other.DivisionNumber)
                    && other.OpenPlay.SharesHoursWith(openPlay));

                if (clashesWithAnOpenPlay)
                {
                    skipped.Add($"{where}: another open play already has those hours.");
                    continue;
                }

                var clashesWithABooking = booked.Any(held =>
                    held.CourtId == bookable.CourtId
                    && Clashes(bookable, held.CourtSportId, held.DivisionNumber)
                    && openPlay.Occupies(held.Date, held.StartsAt, held.EndsAt));

                if (clashesWithABooking)
                {
                    skipped.Add($"{where}: a booking already has some of those hours.");
                    continue;
                }

                openPlay.MarkSeeded(now);

                // Published straight away: samples are there to be seen on the
                // public pages, and a draft is not.
                openPlay.Publish(createdBy, now);
                db.OpenPlays.Add(openPlay);
                running.Add((openPlay, bookable.CourtSportId, bookable.DivisionNumber));
                built.Add((openPlay, facility.Name, bookable.CourtName, bookable.SportName));
            }
        }

        if (built.Count > 0)
        {
            audit.RecordEvent(
                actor,
                AuditAction.SeededOpenPlaysBuilt,
                AuditEntityType.OpenPlay,
                built[0].OpenPlay.Id,
                new Dictionary<string, string?>
                {
                    ["openPlays"] = built.Count.ToString(CultureInfo.InvariantCulture),
                    ["venues"] = facilities.Count.ToString(CultureInfo.InvariantCulture)
                },
                "Sample open plays built for demonstration.");
        }

        await db.SaveChangesAsync(ct);

        return new OpenPlaySeedResult(
            facilities.Count,
            [.. built.Select(entry => Summarize(entry.OpenPlay, entry.Facility, entry.Court, entry.Sport, 0))],
            skipped);
    }

    public async Task<IReadOnlyCollection<SeededOpenPlaySummary>> SeededAsync(CancellationToken ct)
    {
        var standing = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlay.SeededAt != null)
            .OrderBy(openPlay => openPlay.Facility.Name)
            .ThenBy(openPlay => openPlay.Title)
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                Facility = openPlay.Facility.Name,
                Court = openPlay.BookableCourt.Court.Name,
                Sport = openPlay.BookableCourt.CourtSport.Sport.Name,
                Registrations = db.OpenPlayRegistrations
                    .Count(registration => registration.Session.OpenPlayId == openPlay.Id)
            })
            .ToListAsync(ct);

        return [.. standing.Select(entry =>
            Summarize(entry.OpenPlay, entry.Facility, entry.Court, entry.Sport, entry.Registrations))];
    }

    public async Task<OpenPlaySeedRemovalResult> RemoveSeededAsync(AuditActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var openPlayIds = await db.OpenPlays
            .Where(openPlay => openPlay.SeededAt != null)
            .Select(openPlay => openPlay.Id)
            .ToArrayAsync(ct);

        if (openPlayIds.Length == 0)
        {
            return new OpenPlaySeedRemovalResult(0, 0, 0);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var removed = await RemoveOpenPlaysAsync(db, openPlayIds, ct);

        audit.RecordEvent(
            actor,
            AuditAction.SeededOpenPlaysRemoved,
            AuditEntityType.OpenPlay,
            openPlayIds[0],
            new Dictionary<string, string?>
            {
                ["openPlays"] = removed.OpenPlays.ToString(CultureInfo.InvariantCulture),
                ["sessions"] = removed.Sessions.ToString(CultureInfo.InvariantCulture),
                ["registrations"] = removed.Registrations.ToString(CultureInfo.InvariantCulture)
            },
            "Sample open plays removed.");

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return removed;
    }

    /// <summary>
    /// Deletes these open plays, child first. Every foreign key under an open
    /// play is Restrict, because a real one with money on it is ended, never
    /// deleted, so nothing here can lean on a cascade. Shared with the venue
    /// seeder, which has to clear a demo venue's open plays before its courts.
    /// </summary>
    internal static async Task<OpenPlaySeedRemovalResult> RemoveOpenPlaysAsync(
        AppDbContext db,
        Guid[] openPlayIds,
        CancellationToken ct)
    {
        var registrations = await db.OpenPlayRegistrations
            .Where(registration => openPlayIds.Contains(registration.Session.OpenPlayId))
            .ExecuteDeleteAsync(ct);

        var sessions = await db.OpenPlaySessions
            .Where(session => openPlayIds.Contains(session.OpenPlayId))
            .ExecuteDeleteAsync(ct);

        var openPlays = await db.OpenPlays
            .Where(openPlay => openPlayIds.Contains(openPlay.Id))
            .ExecuteDeleteAsync(ct);

        return new OpenPlaySeedRemovalResult(openPlays, sessions, registrations);
    }

    /// <summary>
    /// The clash rule from <see cref="BookableCourt.ClashesWith"/>, asked of
    /// ids. The caller has already narrowed to one floor.
    /// </summary>
    private static bool Clashes(Bookable bookable, Guid courtSportId, int divisionNumber) =>
        bookable.CourtSportId != courtSportId || bookable.DivisionNumber == divisionNumber;

    private static SeededOpenPlaySummary Summarize(
        OpenPlay openPlay, string facility, string court, string sport, int registrations) =>
        new(
            openPlay.Id,
            openPlay.Title,
            facility,
            court,
            sport,
            openPlay.Level,
            openPlay.Days.ToString(),
            openPlay.StartsAt,
            openPlay.EndsAt,
            openPlay.RegistrationFee,
            openPlay.MaxPlayers,
            registrations);

    private sealed record Sample(
        string Title,
        string SportKey,
        string CourtName,
        int DivisionNumber,
        string Level,
        int MaxPlayers,
        decimal Fee,
        OpenPlayDays Days,
        TimeOnly StartsAt,
        TimeOnly EndsAt,
        int CutoffMinutes,
        OpenPlayEarlyBird? EarlyBird,
        int? Weeks);

    private sealed record Bookable(
        Guid Id,
        Guid CourtId,
        Guid CourtSportId,
        int DivisionNumber,
        string CourtName,
        string SportKey,
        string SportName);

    private sealed record Held(
        Guid CourtId,
        Guid CourtSportId,
        int DivisionNumber,
        DateOnly Date,
        TimeOnly StartsAt,
        TimeOnly EndsAt);
}
