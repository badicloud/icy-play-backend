using IcyPlay.Application.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// The desk's reports across the platform, for the admin.
///
/// Every figure comes from the same code the desk uses, handed a different list
/// of venues. Two copies of "how busy is a court" — one for the owner, one for
/// the platform — would be two answers to the question the owner and the
/// platform most need to agree on.
/// </summary>
public sealed class PlatformReportService(AppDbContext db, TimeProvider timeProvider) : IPlatformReportService
{
    public async Task<IReadOnlyCollection<ReportOwner>> OwnersAsync(CancellationToken ct)
    {
        var owners = await db.FacilityOwners
            .AsNoTracking()
            .OrderBy(owner => owner.BusinessName)
            .Select(owner => new
            {
                owner.Id,
                owner.BusinessName,
                Venues = db.Facilities
                    .Where(facility => facility.FacilityOwnerId == owner.Id)
                    .OrderBy(facility => facility.Name)
                    .Select(facility => new DeskVenue(facility.Id, facility.Name))
                    .ToList()
            })
            .ToListAsync(ct);

        return [.. owners.Select(owner => new ReportOwner(owner.Id, owner.BusinessName, owner.Venues))];
    }

    public async Task<PlatformReportResult<PlatformSnapshot>> SnapshotAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        CancellationToken ct)
    {
        var scope = await ScopeAsync(facilityOwnerId, facilityId, ct);

        if (scope.Failure != PlatformReportFailure.None)
        {
            return PlatformReportResult<PlatformSnapshot>.Fail(scope.Failure);
        }

        var utcNow = timeProvider.GetUtcNow();
        var venues = scope.Venues;
        var perOwner = new List<OwnerSnapshot>();

        foreach (var owner in scope.Owners)
        {
            var theirs = venues.Where(venue => venue.OwnerId == owner.Id).Select(venue => venue.Id).ToList();

            perOwner.Add(new OwnerSnapshot(
                owner.Id,
                owner.BusinessName,
                theirs.Count,
                await RightNow.ReadAsync(db, theirs, utcNow, ct)));
        }

        return PlatformReportResult<PlatformSnapshot>.Success(new PlatformSnapshot(
            scope.Owners.Count,
            venues.Count,
            await RightNow.ReadAsync(db, [.. venues.Select(venue => venue.Id)], utcNow, ct),
            perOwner));
    }

    public async Task<PlatformReportResult<UtilizationReport>> UtilizationAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (CheckRange(from, to) is var wrong and not PlatformReportFailure.None)
        {
            return PlatformReportResult<UtilizationReport>.Fail(wrong);
        }

        var scope = await ScopeAsync(facilityOwnerId, facilityId, ct);

        if (scope.Failure != PlatformReportFailure.None)
        {
            return PlatformReportResult<UtilizationReport>.Fail(scope.Failure);
        }

        var venueIds = scope.Venues.Select(venue => venue.Id).ToList();

        // Every venue counts as "owned" here: the rental is the admin's to see,
        // the same figure each owner sees of their own.
        return PlatformReportResult<UtilizationReport>.Success(
            await Utilization.ReadAsync(db, venueIds, venueIds, new UtilizationQuery(from, to), ct));
    }

    public async Task<PlatformReportResult<HoursOverTime>> HoursOverTimeAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!HoursGrain.IsSupported(query.Grain))
        {
            return PlatformReportResult<HoursOverTime>.Fail(PlatformReportFailure.UnknownGrain);
        }

        if (CheckRange(query.From, query.To) is var wrong and not PlatformReportFailure.None)
        {
            return PlatformReportResult<HoursOverTime>.Fail(wrong);
        }

        var scope = await ScopeAsync(facilityOwnerId, facilityId, ct);

        if (scope.Failure != PlatformReportFailure.None)
        {
            return PlatformReportResult<HoursOverTime>.Fail(scope.Failure);
        }

        return PlatformReportResult<HoursOverTime>.Success(
            await Utilization.OverTimeAsync(db, [.. scope.Venues.Select(venue => venue.Id)], query, ct));
    }

    public Task<PlatformReportResult<MovesReport>> MovesAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct) =>
        RunAsync(
            facilityOwnerId,
            facilityId,
            Over(query),
            venues => Moves.ReadAsync(db, venues, query, ct),
            ct);

    public Task<PlatformReportResult<DeclinesReport>> DeclinesAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct) =>
        RunAsync(
            facilityOwnerId,
            facilityId,
            Over(query),
            venues => Declines.ReadAsync(db, venues, query, maySeeMoney: true, ct),
            ct);

    public Task<PlatformReportResult<TakingsReport>> TakingsAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct)
    {
        // Five years, not one: takings are compared year on year.
        var wrong = Over(query, TakingsReport.MostDays);

        return RunAsync(
            facilityOwnerId,
            facilityId,
            wrong == PlatformReportFailure.WindowTooWide ? PlatformReportFailure.TakingsWindowTooWide : wrong,
            venues => Takings.ReadAsync(db, venues, query, ct),
            ct);
    }

    public Task<PlatformReportResult<MissedReport>> MissedAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct) =>
        RunAsync(
            facilityOwnerId,
            facilityId,
            Over(query),
            venues => Missed.ReadAsync(db, venues, query, timeProvider.GetUtcNow(), ct),
            ct);

    public async Task<PlatformReportResult<CourtChangesReport>> CourtChangesAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        CourtChangesQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await RunAsync(
            facilityOwnerId,
            facilityId,
            CheckRange(query.From, query.To),
            async venues =>
            {
                // A court asked for by id has to be at one of these venues.
                if (query.CourtId is Guid courtId
                    && !await db.Courts.AnyAsync(court => court.Id == courtId && venues.Contains(court.FacilityId), ct))
                {
                    return null;
                }

                return await CourtChanges.ReadAsync(db, venues, query, timeProvider.GetUtcNow(), ct);
            },
            ct,
            missing: PlatformReportFailure.CourtNotFound);
    }

    public Task<PlatformReportResult<CourtMixReport>> CourtMixAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        CourtMixQuery query,
        CancellationToken ct) =>
        RunAsync(
            facilityOwnerId,
            facilityId,
            CheckRange(query.From, query.To),
            venues => CourtMix.ReadAsync(db, venues, query, ct),
            ct);

    /// <summary>
    /// Every report the same way round: refuse a bad range, work out the
    /// venues, refuse an owner or venue that is not there, then hand the
    /// venues to the desk's own report. A report that answers null means the
    /// thing it was narrowed to is not in scope, which is <paramref name="missing"/>.
    /// </summary>
    private async Task<PlatformReportResult<T>> RunAsync<T>(
        Guid? facilityOwnerId,
        Guid? facilityId,
        PlatformReportFailure rangeFailure,
        Func<List<Guid>, Task<T?>> read,
        CancellationToken ct,
        PlatformReportFailure missing = PlatformReportFailure.None)
        where T : class
    {
        if (rangeFailure != PlatformReportFailure.None)
        {
            return PlatformReportResult<T>.Fail(rangeFailure);
        }

        var scope = await ScopeAsync(facilityOwnerId, facilityId, ct);

        if (scope.Failure != PlatformReportFailure.None)
        {
            return PlatformReportResult<T>.Fail(scope.Failure);
        }

        var report = await read([.. scope.Venues.Select(venue => venue.Id)]);

        return report is null
            ? PlatformReportResult<T>.Fail(missing)
            : PlatformReportResult<T>.Success(report);
    }

    /// <summary>An over-time report's limits: a known grain, forwards, and at most <paramref name="mostDays"/>.</summary>
    private static PlatformReportFailure Over(HoursQuery query, int mostDays = UtilizationQueryValidator.MostDays)
    {
        ArgumentNullException.ThrowIfNull(query);

        return HoursGrain.IsSupported(query.Grain)
            ? CheckRange(query.From, query.To, mostDays)
            : PlatformReportFailure.UnknownGrain;
    }

    /// <summary>The desk's own limits: forwards, and a year at most.</summary>
    private static PlatformReportFailure CheckRange(DateOnly from, DateOnly to, int mostDays = UtilizationQueryValidator.MostDays)
    {
        if (to < from)
        {
            return PlatformReportFailure.WindowBackwards;
        }

        return to.DayNumber - from.DayNumber >= mostDays
            ? PlatformReportFailure.WindowTooWide
            : PlatformReportFailure.None;
    }

    /// <summary>
    /// The owners and venues a report covers: all of them, one owner's, or one
    /// venue. An owner or venue that does not exist is a failure rather than an
    /// empty report — an admin reading zeros for a typo would believe them.
    /// </summary>
    private async Task<Scope> ScopeAsync(Guid? facilityOwnerId, Guid? facilityId, CancellationToken ct)
    {
        var owners = await db.FacilityOwners
            .AsNoTracking()
            .Where(owner => facilityOwnerId == null || owner.Id == facilityOwnerId)
            .OrderBy(owner => owner.BusinessName)
            .Select(owner => new Owner(owner.Id, owner.BusinessName))
            .ToListAsync(ct);

        if (facilityOwnerId is not null && owners.Count == 0)
        {
            return new Scope([], [], PlatformReportFailure.OwnerNotFound);
        }

        var ownerIds = owners.ConvertAll(owner => owner.Id);

        var venues = await db.Facilities
            .AsNoTracking()
            .Where(facility => ownerIds.Contains(facility.FacilityOwnerId)
                && (facilityId == null || facility.Id == facilityId))
            .Select(facility => new Venue(facility.Id, facility.FacilityOwnerId))
            .ToListAsync(ct);

        if (facilityId is not null)
        {
            if (venues.Count == 0)
            {
                return new Scope([], [], PlatformReportFailure.VenueNotFound);
            }

            // One venue is one owner's, whichever owner filter came with it.
            owners = [.. owners.Where(owner => owner.Id == venues[0].OwnerId)];
        }

        return new Scope(owners, venues, PlatformReportFailure.None);
    }

    private sealed record Owner(Guid Id, string BusinessName);

    private sealed record Venue(Guid Id, Guid OwnerId);

    private sealed record Scope(
        IReadOnlyList<Owner> Owners,
        IReadOnlyList<Venue> Venues,
        PlatformReportFailure Failure);
}
