using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// What a venue has right now, counted by venue type and by what each court is
/// set up for.
///
/// How much each venue type sold is not worked out here: it is the utilization
/// report's own figures, court by court, added up by roof. One sum, so "indoor
/// sold 40%" here and Court utilisation for the same courts cannot disagree.
/// </summary>
internal static class CourtMix
{
    private static readonly string[] VenueTypeOrder =
        [CourtVenueType.Indoor, CourtVenueType.Covered, CourtVenueType.Outdoor];

    public static async Task<CourtMixReport> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        CourtMixQuery query,
        CancellationToken ct)
    {
        var courts = await db.Courts
            .AsNoTracking()
            .Where(court => venueIds.Contains(court.FacilityId))
            .Include(court => court.Facility)
            .Include(court => court.Sports).ThenInclude(pair => pair.Sport)
            .Include(court => court.Sports).ThenInclude(pair => pair.BookableCourts)
            .OrderBy(court => court.Facility.Name)
            .ThenBy(court => court.DisplayOrder)
            .ThenBy(court => court.Name)
            .ToListAsync(ct);

        // No money wanted, so no venue counts as owned: the figures come back
        // without it, which is all this needs.
        var usage = (await Utilization.ReadAsync(
                db,
                venueIds,
                [],
                new UtilizationQuery(query.From, query.To),
                ct))
            .Courts
            .ToDictionary(court => court.CourtId);

        var live = courts.Where(court => court.IsActive).ToList();

        static int Parts(Court court) =>
            court.Sports.Sum(pair => pair.BookableCourts.Count(unit => unit.IsActive));

        var venueTypes = live
            .GroupBy(court => court.VenueType)
            .OrderBy(group => Array.IndexOf(VenueTypeOrder, group.Key) is var at && at < 0 ? int.MaxValue : at)
            .Select(group => new VenueTypeMix(
                group.Key,
                group.Count(),
                [.. group.Select(court => court.Name)],
                group.Sum(court => usage.GetValueOrDefault(court.Id)?.OpenMinutes ?? 0),
                group.Sum(court => usage.GetValueOrDefault(court.Id)?.InUseMinutes ?? 0)))
            .ToList();

        var activities = live
            .SelectMany(court => court.Sports)
            .GroupBy(pair => pair.SportId)
            .Select(group =>
            {
                var sport = group.First().Sport;

                return new ActivityMix(
                    sport.Id,
                    sport.Name,
                    sport.Kind,
                    group.Count(),
                    group.Sum(pair => pair.BookableCourts.Count(unit => unit.IsActive)),
                    group.Count(pair => pair.IsPrimary));
            })
            // Sports before events, and within each the ones most courts take first.
            .OrderBy(activity => activity.Kind == ActivityKind.Event)
            .ThenByDescending(activity => activity.Courts)
            .ThenBy(activity => activity.Name)
            .ToList();

        var takeEvents = live.Count(court => court.Sports.Any(pair => pair.Sport.IsEvent));

        var rows = courts
            .Where(court => court.IsActive || query.IncludeRetired)
            .Select(court => new CourtMixRow(
                court.Id,
                court.FacilityId,
                court.Facility.Name,
                court.Name,
                court.VenueType,
                court.Surface,
                court.HasLighting,
                !court.IsActive,
                Parts(court),
                [
                    .. court.Sports
                        .OrderByDescending(pair => pair.IsPrimary)
                        .ThenBy(pair => pair.Sport.IsEvent)
                        .ThenBy(pair => pair.Sport.Name)
                        .Select(pair => new CourtActivity(
                            pair.Sport.Name,
                            pair.Sport.Kind,
                            pair.IsPrimary,
                            pair.Divisions))
                ],
                usage.GetValueOrDefault(court.Id)?.OpenMinutes ?? 0,
                usage.GetValueOrDefault(court.Id)?.InUseMinutes ?? 0))
            .ToList();

        return new CourtMixReport(
            query.From,
            query.To,
            new CourtMixSummary(
                live.Count,
                live.Sum(Parts),
                live.Count(court => court.VenueType is CourtVenueType.Indoor or CourtVenueType.Covered),
                live.Count(court => court.HasLighting),
                takeEvents,
                activities.Count(activity => activity.Kind == ActivityKind.Event),
                courts.Count(court => !court.IsActive)),
            venueTypes,
            activities,
            rows);
    }
}
