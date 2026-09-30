using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// The open plays the public site lists, each with its next few dates.
///
/// The dates are worked out from the series rule here instead of read from
/// rows, because a date only gets a session row once somebody registers.
/// </summary>
public sealed class OpenPlayCatalog(AppDbContext db, TimeProvider timeProvider) : IOpenPlayCatalog
{
    /// <summary>How far ahead the listing looks for dates.</summary>
    private const int LookAheadDays = 28;

    /// <summary>How many upcoming dates each card shows.</summary>
    private const int SessionsShown = 4;

    public async Task<IReadOnlyCollection<CatalogOpenPlay>> ListAsync(
        string? sport,
        Guid? facility,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // A day's slack either side of UTC covers every venue's own today.
        // The exact cut is made per venue below, on its own clock.
        var roughToday = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1);

        var rows = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay =>
                // Published only: a draft is still being worked on. An ended one
                // stays listed until its last date, which End has set.
                openPlay.PublishedAt != null
                && (openPlay.EndDate == null || openPlay.EndDate >= roughToday)
                && (sport == null || openPlay.BookableCourt.CourtSport.Sport.Key == sport)
                && (facility == null || openPlay.FacilityId == facility)
                && openPlay.BookableCourt.IsActive
                && openPlay.BookableCourt.Court.IsActive
                && openPlay.BookableCourt.CourtSport.Sport.IsActive
                && openPlay.Facility.IsActive
                && openPlay.Facility.FacilityOwner.IsActive)
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                FacilityName = openPlay.Facility.Name,
                openPlay.Facility.City,
                openPlay.Facility.TimeZone,
                OwnerId = openPlay.Facility.FacilityOwnerId,
                CourtName = openPlay.BookableCourt.Court.Name,
                SportKey = openPlay.BookableCourt.CourtSport.Sport.Key,
                SportName = openPlay.BookableCourt.CourtSport.Sport.Name,
                openPlay.BookableCourt.CourtSport.SportId
            })
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var ownerIds = rows.Select(row => row.OwnerId).Distinct().ToArray();
        var openPlayIds = rows.Select(row => row.OpenPlay.Id).ToArray();
        var facilityIds = rows.Select(row => row.OpenPlay.FacilityId).Distinct().ToArray();
        var lastDate = roughToday.AddDays(LookAheadDays + 2);

        // The live contract is where the platform top-up comes from, and a
        // venue without one is not live, the same rule the court catalogue uses.
        var contracts = await db.FacilityOwnerContracts
            .AsNoTracking()
            .Where(contract => ownerIds.Contains(contract.FacilityOwnerId)
                && contract.CancelledAt == null
                && contract.StartDate <= lastDate
                && contract.EndDate >= roughToday)
            .ToListAsync(ct);

        var sessions = await db.OpenPlaySessions
            .AsNoTracking()
            .Where(session => openPlayIds.Contains(session.OpenPlayId) && session.Date >= roughToday)
            .Include(session => session.Registrations)
            .ToListAsync(ct);

        var closures = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period => facilityIds.Contains(period.FacilityId) && period.LiftedAt == null)
            .ToListAsync(ct);

        // The photos an open play without its own falls back on, cover first
        // then as the venue arranged them, as the court listing orders them.
        var courtIds = rows.Select(row => row.OpenPlay.CourtId).Distinct().ToArray();

        var photos = await db.Photos
            .AsNoTracking()
            .Where(photo =>
                (photo.CourtId != null && courtIds.Contains(photo.CourtId.Value))
                || (photo.CourtId == null && facilityIds.Contains(photo.FacilityId)))
            .OrderByDescending(photo => photo.IsCover)
            .ThenBy(photo => photo.DisplayOrder)
            .Select(photo => new { photo.FacilityId, photo.CourtId, photo.SportId, photo.SecureUrl })
            .ToListAsync(ct);

        string? FallbackPhoto(Guid facilityId, Guid courtId, Guid sportId) =>
            photos.FirstOrDefault(photo => photo.CourtId == courtId && photo.SportId == sportId)?.SecureUrl
            ?? photos.FirstOrDefault(photo => photo.CourtId == courtId)?.SecureUrl
            ?? photos.FirstOrDefault(photo => photo.CourtId == null && photo.FacilityId == facilityId)?.SecureUrl;

        var listed = new List<CatalogOpenPlay>();

        foreach (var row in rows)
        {
            var openPlay = row.OpenPlay;
            var venueNow = VenueClock.LocalNowIn(row.TimeZone, now).DateTime;
            var today = DateOnly.FromDateTime(venueNow);

            var contract = contracts.FirstOrDefault(candidate =>
                candidate.FacilityOwnerId == row.OwnerId && candidate.Covers(today));

            if (contract is null)
            {
                continue;
            }

            var platformFee = contract.PlatformHourlyRate;
            var upcoming = new List<CatalogOpenPlaySession>();

            for (var date = today; date <= today.AddDays(LookAheadDays) && upcoming.Count < SessionsShown; date = date.AddDays(1))
            {
                if (!openPlay.RunsOn(date) || venueNow >= date.ToDateTime(openPlay.EndsAt))
                {
                    continue;
                }

                var session = sessions.FirstOrDefault(candidate =>
                    candidate.OpenPlayId == openPlay.Id && candidate.Date == date);

                // A cancelled date is released, and a closed court is closed.
                if (session is { IsCancelled: true } || IsClosed(closures, openPlay, row.TimeZone, date))
                {
                    continue;
                }

                var price = openPlay.PriceFor(date, venueNow, platformFee);

                upcoming.Add(new CatalogOpenPlaySession(
                    date,
                    session?.SpotsLeftAt(now, openPlay.MaxPlayers) ?? openPlay.MaxPlayers,
                    openPlay.RegistrationClosesAt(date),
                    openPlay.IsOpenForRegistration(date, venueNow),
                    price.Total,
                    price.Discount > 0));
            }

            if (upcoming.Count == 0)
            {
                continue;
            }

            listed.Add(new CatalogOpenPlay(
                openPlay.Id,
                openPlay.Title,
                openPlay.FacilityId,
                row.FacilityName,
                row.City,
                row.CourtName,
                row.SportKey,
                row.SportName,
                openPlay.Level,
                DayNames(openPlay.Days),
                openPlay.StartsAt,
                openPlay.EndsAt,
                openPlay.StartDate,
                openPlay.EndDate,
                openPlay.MaxPlayers,
                openPlay.RegistrationFee,
                platformFee,
                openPlay.RegistrationFee + platformFee,
                openPlay.RegistrationCutoffMinutes,
                openPlay.EarlyBird is { } earlyBird
                    ? new CatalogEarlyBird(earlyBird.DiscountKind, earlyBird.DiscountValue, earlyBird.LeadMinutes)
                    : null,
                upcoming,
                openPlay.CoverPhotoUrl ?? FallbackPhoto(openPlay.FacilityId, openPlay.CourtId, row.SportId)));
        }

        return [.. listed
            .OrderBy(entry => entry.UpcomingSessions.First().Date)
            .ThenBy(entry => entry.StartsAt)
            .ThenBy(entry => entry.Title)];
    }

    /// <summary>
    /// Whether a maintenance closure touches the session's hours, for the
    /// whole facility or for this court.
    /// </summary>
    private static bool IsClosed(
        IEnumerable<MaintenancePeriod> closures,
        OpenPlay openPlay,
        string timeZone,
        DateOnly date)
    {
        var starts = AtVenue(date.ToDateTime(openPlay.StartsAt), timeZone);
        var ends = AtVenue(date.ToDateTime(openPlay.EndsAt), timeZone);

        return closures.Any(period =>
            period.FacilityId == openPlay.FacilityId
            && (period.CourtId is null || period.CourtId == openPlay.CourtId)
            && period.StartsAt < ends
            && (period.EndsAt is null || starts < period.EndsAt));
    }

    private static DateTimeOffset AtVenue(DateTime wallClock, string timeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock))
            : new DateTimeOffset(wallClock, TimeSpan.Zero);

    private static IReadOnlyCollection<string> DayNames(OpenPlayDays days) =>
        [.. Enum.GetValues<DayOfWeek>().Where(day => days.Includes(day)).Select(day => day.ToString())];
}
