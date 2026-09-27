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
