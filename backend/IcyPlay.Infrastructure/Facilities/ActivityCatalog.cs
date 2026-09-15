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

    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    public Task<IReadOnlyCollection<CatalogActivity>> ListAsync(CancellationToken ct) =>
        CachedAsync(ActivitiesKey, () => QueryActivitiesAsync(ct));

    public Task<IReadOnlyCollection<CatalogCourt>> ListCourtsAsync(
        string? sportKey,
        CancellationToken ct)
    {
        var key = sportKey?.Trim().ToLowerInvariant();
        // No sport named means everything on offer, which is what the landing
        // page shows before a visitor has chosen anything.
        var narrowed = string.IsNullOrEmpty(key) ? null : key;

        return CachedAsync(
            $"public:catalog-courts:{narrowed ?? "*"}",
            () => QueryCourtsAsync(narrowed, ct));
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
        var offerings = await ListCourtsAsync(key, ct);
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
                SportPhotoUrl = db.Photos
                    .Where(photo => photo.CourtId == link.CourtId && photo.SportId == link.SportId)
                    .OrderByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.DisplayOrder)
                    .Select(photo => photo.SecureUrl)
                    .FirstOrDefault(),
                CoverPhotoUrl = db.Photos
                    .Where(photo => photo.CourtId == link.CourtId && photo.IsCover)
                    .Select(photo => photo.SecureUrl)
                    .FirstOrDefault(),
                SportImageUrl = link.Sport.ImageSecureUrl,
                // Either level closes it: a facility shut for the week takes
                // every court in it down.
                Closure = db.MaintenancePeriods
                    .Where(period =>
                        period.LiftedAt == null &&
                        period.StartsAt <= now &&
                        (period.EndsAt == null || now < period.EndsAt) &&
                        (period.CourtId == link.CourtId ||
                            (period.CourtId == null && period.FacilityId == link.Court.FacilityId)))
                    // The facility closure wins when both apply: it is the wider
                    // truth, and the one the customer needs to hear.
                    .OrderBy(period => period.CourtId == null ? 0 : 1)
                    .Select(period => new { period.EndsAt, WholeVenue = period.CourtId == null })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

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
                        row.SportPhotoUrl ?? row.CoverPhotoUrl ?? row.SportImageUrl,
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
                        row.Closure != null,
                        row.Closure == null ? null : row.Closure.EndsAt,
                        row.Closure != null && row.Closure.WholeVenue)))
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
