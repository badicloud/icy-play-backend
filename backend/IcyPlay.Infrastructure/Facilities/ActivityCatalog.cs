using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// The public catalogue, cached. Every visitor to the landing page asks the
/// same question and the answer changes only when an admin configures a court,
/// so it is counted once and held until either the clock or an edit says
/// otherwise.
///
/// The expiry is a backstop rather than the mechanism: it catches a change that
/// slipped past the explicit clear, which is the sort of thing that happens the
/// moment a new write path is added.
/// </summary>
public sealed class ActivityCatalog(
    AppDbContext db,
    IMemoryCache cache,
    CatalogCacheSignal signal,
    TimeProvider timeProvider,
    ILogger<ActivityCatalog> logger) : IActivityCatalog
{
    private const string ActivitiesKey = "public:activity-catalog";

    private const string FacilitiesKey = "public:facility-catalog";

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    public Task<IReadOnlyCollection<CatalogActivity>> ListAsync(CancellationToken ct) =>
        CachedAsync(ActivitiesKey, () => QueryActivitiesAsync(ct));

    public Task<IReadOnlyCollection<CatalogFacility>> ListFacilitiesAsync(CancellationToken ct) =>
        CachedAsync(FacilitiesKey, () => QueryFacilitiesAsync(ct));

    public async Task<IReadOnlyCollection<CatalogCourt>> ListCourtsAsync(
        string? sportKey,
        Guid? facilityId,
        CancellationToken ct)
    {
        var key = sportKey?.Trim().ToLowerInvariant();
        // No sport named means everything on offer, which is what the landing
        // page shows before a visitor has chosen anything.
        var narrowed = string.IsNullOrEmpty(key) ? null : key;

        var offerings = await CachedAsync(
            $"public:catalog-courts:{narrowed ?? "*"}",
            () => QueryCourtsAsync(narrowed, ct));

        // The venue is sieved out of the cached list rather than asked of the
        // database. The list is already held whole and is cheap to walk, and a
        // cache key per venue would multiply the entries by the number of
        // venues for an answer that is a subset of one we already have. It also
        // means a venue's own page and the landing page can never disagree
        // about the same court.
        return facilityId is Guid venue
            ? [.. offerings.Where(court => court.FacilityId == venue)]
            : offerings;
    }

    public async Task<CatalogCourtDetail?> GetCourtAsync(
        Guid courtId,
        string sportKey,
        int divisionNumber,
        CancellationToken ct)
    {
        var key = sportKey.Trim().ToLowerInvariant();

        if (key.Length == 0 || divisionNumber < 1)
        {
            return null;
        }

        // The listing already decides what is on offer and how a division is
        // named. Reading through it means the card and the page can never
        // disagree about the same court.
        var offerings = await ListCourtsAsync(key, null, ct);
        var court = offerings.FirstOrDefault(candidate =>
            candidate.CourtId == courtId && candidate.DivisionNumber == divisionNumber);

        if (court is null)
        {
            return null;
        }

        return await CachedDetailAsync(
            $"public:catalog-court:{courtId}:{key}:{divisionNumber}",
            () => QueryDetailAsync(court, ct));
    }

    public void Invalidate() => signal.Clear();

    /// <summary>
    /// The single-value twin of <see cref="CachedAsync{T}"/>. Registered against
    /// the same signal, so one admin edit clears the pages as well as the lists.
    /// </summary>
    private async Task<CatalogCourtDetail?> CachedDetailAsync(
        string key,
        Func<Task<CatalogCourtDetail>> load)
    {
        if (cache.TryGetValue(key, out CatalogCourtDetail? cached) && cached is not null)
        {
            return cached;
        }

        var loaded = await load();

        using var entry = cache.CreateEntry(key);
        entry.Value = loaded;
        entry.AbsoluteExpirationRelativeToNow = Expiry;
        entry.AddExpirationToken(new CancellationChangeToken(signal.Token));

        return loaded;
    }

    private async Task<CatalogCourtDetail> QueryDetailAsync(CatalogCourt court, CancellationToken ct)
    {
        // The page is a court seen through one sport, so its gallery leads with
        // the pictures of the floor marked out for that sport. The rest follow:
        // a customer still wants to see the hall, the lighting and the seats.
        var sportKey = court.SportKey;

        var row = await db.Courts
            .AsNoTracking()
            .Where(candidate => candidate.Id == court.CourtId)
            .Select(candidate => new
            {
                candidate.Description,
                candidate.SizeLabel,
                candidate.Capacity,
                candidate.Equipment,
                candidate.BufferMinutes,
                CourtPhotos = db.Photos
                    .Where(photo => photo.CourtId == candidate.Id)
                    .OrderByDescending(photo => photo.Sport != null && photo.Sport.Key == sportKey)
                    .ThenByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.DisplayOrder)
                    .Select(photo => new PhotoItem(
                        photo.Id,
                        photo.PublicId,
                        photo.SecureUrl,
                        photo.Caption,
                        photo.DisplayOrder,
                        photo.IsCover,
                        photo.SportId))
                    .ToList(),
                Venue = new
                {
                    candidate.Facility.Description,
                    candidate.Facility.SafetyMeasures,
                    candidate.Facility.HouseRules,
                    candidate.Facility.TimeZone,
                    candidate.Facility.ContactPhone,
                    candidate.Facility.ContactEmail,
                    Amenities = candidate.Facility.Amenities
                        .OrderBy(link => link.Amenity.Category)
                        .ThenBy(link => link.Amenity.DisplayOrder)
                        .Select(link => link.Amenity.Name)
                        .ToList(),
                    Photos = db.Photos
                        .Where(photo => photo.FacilityId == candidate.FacilityId && photo.CourtId == null)
                        .OrderByDescending(photo => photo.IsCover)
                        .ThenBy(photo => photo.DisplayOrder)
                        .Select(photo => new PhotoItem(
                            photo.Id,
                            photo.PublicId,
                            photo.SecureUrl,
                            photo.Caption,
                            photo.DisplayOrder,
                            photo.IsCover))
                        .ToList()
                }
            })
            .SingleAsync(ct);

        // Read the same way the admin console resolves them: the court's own
        // hours when it keeps them, otherwise the facility's.
        var hours = await ResolveHoursAsync(court.CourtId, court.FacilityId, ct);

        return new CatalogCourtDetail(
            court,
            row.Description,
            row.SizeLabel,
            row.Capacity,
            row.Equipment,
            row.BufferMinutes,
            row.CourtPhotos,
            new CatalogVenue(
                row.Venue.Description,
                row.Venue.SafetyMeasures,
                row.Venue.HouseRules,
                row.Venue.TimeZone,
                row.Venue.ContactPhone,
                row.Venue.ContactEmail,
                row.Venue.Amenities,
                hours,
                row.Venue.Photos));
    }

    private async Task<IReadOnlyCollection<FacilityOperatingHourDetail>> ResolveHoursAsync(
        Guid courtId,
        Guid facilityId,
        CancellationToken ct)
    {
        var usesFacilityHours = await db.Courts
            .AsNoTracking()
            .Where(court => court.Id == courtId)
            .Select(court => court.UsesFacilityHours)
            .SingleAsync(ct);

        return usesFacilityHours
            ? await db.FacilityOperatingHours
                .AsNoTracking()
                .Where(hour => hour.FacilityId == facilityId)
                .OrderBy(hour => hour.DayOfWeek)
                .Select(hour => new FacilityOperatingHourDetail(
                    (int)hour.DayOfWeek,
                    hour.OpensAt,
                    hour.ClosesAt))
                .ToArrayAsync(ct)
            : await db.CourtOperatingHours
                .AsNoTracking()
                .Where(hour => hour.CourtId == courtId)
                .OrderBy(hour => hour.DayOfWeek)
                .Select(hour => new FacilityOperatingHourDetail(
                    (int)hour.DayOfWeek,
                    hour.OpensAt,
                    hour.ClosesAt))
                .ToArrayAsync(ct);
    }

    /// <summary>
    /// Every cached answer is registered against the same signal, so one admin
    /// edit clears the activity list and every sport's court list together.
    /// Holding one and not the others is how a filter comes to offer a sport
    /// whose only court has just been taken down.
    /// </summary>
    private async Task<IReadOnlyCollection<T>> CachedAsync<T>(
        string key,
        Func<Task<IReadOnlyCollection<T>>> load)
    {
        if (cache.TryGetValue(key, out IReadOnlyCollection<T>? cached) && cached is not null)
        {
            return cached;
        }

        var loaded = await load();

        using var entry = cache.CreateEntry(key);
        entry.Value = loaded;
        entry.AbsoluteExpirationRelativeToNow = Expiry;
        entry.AddExpirationToken(new CancellationChangeToken(signal.Token));

        return loaded;
    }

    /// <summary>
    /// Every part of every court set up for one sport. A court divided three
    /// ways becomes three rows, because three games can run on it at once and
    /// each is booked on its own.
    /// </summary>
    private async Task<IReadOnlyCollection<CatalogCourt>> QueryCourtsAsync(
        string? sportKey,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var now = timeProvider.GetUtcNow();

        var rows = await db.CourtSports
            .AsNoTracking()
            .Where(link =>
                (sportKey == null || link.Sport.Key == sportKey) &&
                link.Sport.IsActive &&
                link.Court.IsActive &&
                link.Court.Facility.IsActive &&
                link.Court.Facility.FacilityOwner.IsActive &&
                link.Court.Facility.FacilityOwner.Contracts.Any(contract =>
                    contract.CancelledAt == null &&
                    contract.StartDate <= today &&
                    today <= contract.EndDate))
            .Select(link => new
            {
                link.CourtId,
                link.SportId,
                CourtName = link.Court.Name,
                link.Court.DisplayOrder,
                link.Court.VenueType,
                link.Court.Surface,
                link.Court.HasLighting,
                link.Court.SlotLengthMinutes,
                link.Court.MinimumDurationMinutes,
                SportKey = link.Sport.Key,
                SportName = link.Sport.Name,
                SportKind = link.Sport.Kind,
                link.Divisions,
                // Ordered here so the parts come back 1, 2, 3 the way counting
                // them out did. Left to the database the order is whatever the
                // index felt like, and a listing that reshuffles between two
                // page loads reads as a broken site.
                Units = link.BookableCourts
                    .Where(unit => unit.IsActive)
                    .OrderBy(unit => unit.DivisionNumber)
                    .Select(unit => new { unit.Id, unit.DivisionNumber })
                    .ToList(),
                link.StandardHourlyRate,
                link.PeakHourlyRate,
                link.WeekendRate,
                link.HolidayRate,
                link.Court.PeakStartsAt,
                link.Court.PeakEndsAt,
                link.Court.PeakOnWeekdays,
                link.Court.PeakOnWeekends,
                link.Court.FacilityId,
                FacilityName = link.Court.Facility.Name,
                link.Court.Facility.AddressLine1,
                link.Court.Facility.City,
                link.Court.Facility.Province,
                link.Court.Facility.PostalCode,
                link.Court.Facility.Latitude,
                link.Court.Facility.Longitude,
                SportImageUrl = link.Sport.ImageSecureUrl,
            })
            .ToListAsync(ct);

        // The photos and the closures, in a query each.
        //
        // They used to hang off the projection above as three correlated
        // subqueries and an OUTER APPLY, which asked the database to answer
        // four questions for every row and gave the planner one shape to get
        // wrong for all of them. Read separately they are plain seeks on
        // indexed columns, and each can be understood on its own.
        //
        // The extra round trips cost nothing worth counting: this whole answer
        // is cached for minutes, and the listing is built once for everybody.
        var courtIds = rows.Select(row => row.CourtId).Distinct().ToArray();
        var facilityIds = rows.Select(row => row.FacilityId).Distinct().ToArray();

        var photos = await db.Photos
            .AsNoTracking()
            .Where(photo =>
                (photo.CourtId != null && courtIds.Contains(photo.CourtId.Value)) ||
                (photo.CourtId == null && facilityIds.Contains(photo.FacilityId)))
            // The order the fallback wants: a cover before the rest, then as
            // the venue arranged them. Chosen here so each lookup below is a
            // first-wins and the rule lives in one place.
            .OrderByDescending(photo => photo.IsCover)
            .ThenBy(photo => photo.DisplayOrder)
            .Select(photo => new
            {
                photo.FacilityId,
                photo.CourtId,
                photo.SportId,
                photo.SecureUrl
            })
            .ToListAsync(ct);

        // This court, set up for this sport.
        var sportPhoto = photos
            .Where(photo => photo.CourtId is not null && photo.SportId is not null)
            .GroupBy(photo => (photo.CourtId!.Value, photo.SportId!.Value))
            .ToDictionary(group => group.Key, group => group.First().SecureUrl);

        // This court, however it was last photographed.
        var courtPhoto = photos
            .Where(photo => photo.CourtId is not null)
            .GroupBy(photo => photo.CourtId!.Value)
            .ToDictionary(group => group.Key, group => group.First().SecureUrl);

        // The building around it.
        var venuePhoto = photos
            .Where(photo => photo.CourtId is null)
            .GroupBy(photo => photo.FacilityId)
            .ToDictionary(group => group.Key, group => group.First().SecureUrl);

        var periods = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period =>
                period.LiftedAt == null &&
                period.StartsAt <= now &&
                (period.EndsAt == null || now < period.EndsAt) &&
                ((period.CourtId != null && courtIds.Contains(period.CourtId.Value)) ||
                    (period.CourtId == null && facilityIds.Contains(period.FacilityId))))
            .Select(period => new
            {
                period.FacilityId,
                period.CourtId,
                period.EndsAt
            })
            .ToListAsync(ct);

        // What is shut, by court. The venue's own closure wins when both apply:
        // it is the wider truth, and the one the customer needs to hear — the
        // same rule the ordering inside the old subquery carried.
        var shut = new Dictionary<Guid, (DateTimeOffset? EndsAt, bool WholeVenue)>();

        foreach (var period in periods.Where(period => period.CourtId is not null))
        {
            shut[period.CourtId!.Value] = (period.EndsAt, false);
        }

        foreach (var row in rows)
        {
            var venueWide = periods.FirstOrDefault(period =>
                period.CourtId is null && period.FacilityId == row.FacilityId);

            if (venueWide is not null)
            {
                shut[row.CourtId] = (venueWide.EndsAt, true);
            }
        }

        // Counting the parts could never come back empty; reading them can. A
        // court configured and priced but missing its roster would drop out of
        // the listing in silence, which is the one way this change can be worse
        // than the arithmetic it replaces. Say so where somebody will see it.
        foreach (var orphan in rows.Where(row => row.Units.Count == 0))
        {
            logger.LogWarning(
                "Court {CourtId} is set up for {SportKey} but has no bookable courts, so it is missing from the public listing.",
                orphan.CourtId,
                orphan.SportKey);
        }

        return
        [
            .. rows
                // Games first, then the floor for hire — the same order the
                // tiles above the list are in. Sorted by name alone, an event
                // lands in the middle of the sports alphabetically and the two
                // kinds read as one muddled list.
                .OrderBy(row => row.SportKind == ActivityKind.Event ? 1 : 0)
                .ThenBy(row => row.FacilityName, StringComparer.Ordinal)
                .ThenBy(row => row.SportName, StringComparer.Ordinal)
                .ThenBy(row => row.DisplayOrder)
                .ThenBy(row => row.CourtName, StringComparer.Ordinal)
                .SelectMany(row => row.Units
                    .Select(unit => new CatalogCourt(
                        unit.Id,
                        row.CourtId,
                        row.SportKey,
                        row.SportName,
                        row.SportKind,
                        unit.DivisionNumber,
                        Court.DivisionName(
                            row.CourtName,
                            row.SportName,
                            unit.DivisionNumber,
                            row.Divisions),
                        row.FacilityId,
                        row.FacilityName,
                        row.AddressLine1,
                        row.City,
                        row.Province,
                        row.PostalCode,
                        row.Latitude,
                        row.Longitude,
                        // Nearest first: this court set up for this sport, then
                        // this court however it was last shot, then the venue
                        // around it, and only then the sport's stock picture.
                        // The venue comes before the stock picture because it is
                        // a photograph of somewhere the customer would actually
                        // be standing.
                        sportPhoto.GetValueOrDefault((row.CourtId, row.SportId))
                            ?? courtPhoto.GetValueOrDefault(row.CourtId)
                            ?? venuePhoto.GetValueOrDefault(row.FacilityId)
                            ?? row.SportImageUrl,
                        row.VenueType,
                        row.Surface,
                        row.HasLighting,
                        row.SlotLengthMinutes,
                        row.MinimumDurationMinutes,
                        row.StandardHourlyRate,
                        row.PeakHourlyRate,
                        row.WeekendRate,
                        row.HolidayRate,
                        row.PeakStartsAt,
                        row.PeakEndsAt,
                        row.PeakOnWeekdays,
                        row.PeakOnWeekends,
                        shut.ContainsKey(row.CourtId),
                        shut.TryGetValue(row.CourtId, out var closure) ? closure.EndsAt : null,
                        shut.TryGetValue(row.CourtId, out var wide) && wide.WholeVenue)))
        ];
    }

    /// <summary>
    /// Every venue with something sellable on it, and what that something is.
    ///
    /// The same test the activity listing uses: an active court, in an active
    /// facility, whose owner has a contract covering today. A venue with no
    /// court configured yet does not appear at all — there would be nothing to
    /// click through to.
    /// </summary>
    private async Task<IReadOnlyCollection<CatalogFacility>> QueryFacilitiesAsync(
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var rows = await db.CourtSports
            .AsNoTracking()
            .Where(link =>
                link.Sport.IsActive &&
                link.Court.IsActive &&
                link.Court.Facility.IsActive &&
                link.Court.Facility.FacilityOwner.IsActive &&
                link.Court.Facility.FacilityOwner.Contracts.Any(contract =>
                    contract.CancelledAt == null &&
                    contract.StartDate <= today &&
                    today <= contract.EndDate))
            .Select(link => new
            {
                link.Court.FacilityId,
                link.Court.Facility.Slug,
                FacilityName = link.Court.Facility.Name,
                link.Court.Facility.AddressLine1,
                link.Court.Facility.City,
                link.Court.Facility.Province,
                link.Court.Facility.PostalCode,
                link.Court.Facility.Latitude,
                link.Court.Facility.Longitude,
                link.Sport.Key,
                SportName = link.Sport.Name,
                link.Sport.Kind,
                link.Sport.DisplayOrder,
                // A court marked out into three is three courts to book.
                CourtCount = link.BookableCourts.Count(unit => unit.IsActive)
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        // One read for the pictures rather than a subquery per venue: the same
        // shape the court listing settled on, and for the same reason.
        var facilityIds = rows.Select(row => row.FacilityId).Distinct().ToArray();

        var photos = await db.Photos
            .AsNoTracking()
            .Where(photo => facilityIds.Contains(photo.FacilityId))
            .OrderByDescending(photo => photo.CourtId == null)
            .ThenByDescending(photo => photo.IsCover)
            .ThenBy(photo => photo.DisplayOrder)
            .Select(photo => new { photo.FacilityId, photo.SecureUrl })
            .ToListAsync(ct);

        var cover = new Dictionary<Guid, string>();

        foreach (var photo in photos)
        {
            // First wins: the ordering above has already put the venue's own
            // cover ahead of a court's, so a venue that has photographed itself
            // is shown as itself rather than as one of its floors.
            cover.TryAdd(photo.FacilityId, photo.SecureUrl);
        }

        return
        [
            .. rows
                .GroupBy(row => row.FacilityId)
                .Select(venue =>
                {
                    var first = venue.First();

                    return new CatalogFacility(
                        venue.Key,
                        first.Slug,
                        first.FacilityName,
                        first.AddressLine1,
                        first.City,
                        first.Province,
                        first.PostalCode,
                        first.Latitude,
                        first.Longitude,
                        cover.GetValueOrDefault(venue.Key),
                        venue.Sum(row => row.CourtCount),
                        [
                            .. venue
                                .GroupBy(row => new { row.Key, row.SportName, row.Kind, row.DisplayOrder })
                                .Select(sport => new
                                {
                                    sport.Key.Key,
                                    sport.Key.SportName,
                                    sport.Key.Kind,
                                    sport.Key.DisplayOrder,
                                    CourtCount = sport.Sum(row => row.CourtCount)
                                })
                                // Sports before events, then whatever there is
                                // most of: a venue with eight pickleball courts
                                // and one party floor is a pickleball venue.
                                .OrderBy(sport => sport.Kind == ActivityKind.Event ? 1 : 0)
                                .ThenByDescending(sport => sport.CourtCount)
                                .ThenBy(sport => sport.DisplayOrder)
                                .ThenBy(sport => sport.SportName, StringComparer.Ordinal)
                                .Select(sport => new CatalogFacilitySport(
                                    sport.Key,
                                    sport.SportName,
                                    sport.Kind,
                                    sport.CourtCount))
                        ]);
                })
                .OrderByDescending(venue => venue.CourtCount)
                .ThenBy(venue => venue.Name, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Configured and sellable: an active court, in an active facility, whose
    /// owner has a contract covering today. Maintenance is deliberately not
    /// counted -- it is temporary, and dropping a sport from the filter because
    /// one venue is resurfacing would hide every other venue that has it.
    /// </summary>
    private async Task<IReadOnlyCollection<CatalogActivity>> QueryActivitiesAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var rows = await db.CourtSports
            .AsNoTracking()
            .Where(link =>
                link.Sport.IsActive &&
                link.Court.IsActive &&
                link.Court.Facility.IsActive &&
                link.Court.Facility.FacilityOwner.IsActive &&
                link.Court.Facility.FacilityOwner.Contracts.Any(contract =>
                    contract.CancelledAt == null &&
                    contract.StartDate <= today &&
                    today <= contract.EndDate))
            .GroupBy(link => new
            {
                link.SportId,
                link.Sport.Key,
                link.Sport.Name,
                link.Sport.Category,
                link.Sport.Kind,
                link.Sport.DisplayOrder
            })
            .Select(group => new
            {
                group.Key.SportId,
                group.Key.Key,
                group.Key.Name,
                group.Key.Category,
                group.Key.Kind,
                group.Key.DisplayOrder,
                // A court marked out into three is three courts to book.
                CourtCount = group.Sum(link => link.BookableCourts.Count(unit => unit.IsActive)),
                FacilityCount = group.Select(link => link.Court.FacilityId).Distinct().Count()
            })
            .ToListAsync(ct);

        return
        [
            .. rows
                .OrderBy(row => row.Kind == ActivityKind.Event ? 1 : 0)
                .ThenByDescending(row => row.CourtCount)
                .ThenBy(row => row.DisplayOrder)
                .ThenBy(row => row.Name, StringComparer.Ordinal)
                .Select(row => new CatalogActivity(
                    row.SportId,
                    row.Key,
                    row.Name,
                    row.Category,
                    row.Kind,
                    row.CourtCount,
                    row.FacilityCount))
        ];
    }
}
