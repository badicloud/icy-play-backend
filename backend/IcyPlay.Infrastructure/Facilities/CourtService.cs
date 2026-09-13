using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Common;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Facilities;

public sealed class CourtService(
    AppDbContext db,
    IAuditLogger audit,
    ICloudinaryAssetService assets,
    TimeProvider timeProvider,
    ILogger<CourtService> logger) : ICourtService
{
    public async Task<CourtResult<CreatedCourtResponse>> CreateAsync(
        CreateCourtRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.FacilityOwnerId, ct);

        if (owner is null)
        {
            return CourtResult<CreatedCourtResponse>.Fail(CourtFailure.FacilityOwnerNotFound);
        }

        var sportIds = request.Court.SportIds.Distinct().ToArray();
        var knownSports = await db.Sports
            .CountAsync(sport => sportIds.Contains(sport.Id) && sport.IsActive, ct);

        if (knownSports != sportIds.Length)
        {
            return CourtResult<CreatedCourtResponse>.Fail(CourtFailure.UnknownSport);
        }

        if (!sportIds.Contains(request.Court.PrimarySportId))
        {
            return CourtResult<CreatedCourtResponse>.Fail(CourtFailure.PrimarySportNotSelected);
        }

        var photos = request.Court.Photos
            .Concat(request.NewFacility?.Photos ?? [])
            .ToArray();

        // The browser posts this metadata, so it is never taken on trust: an
        // http URL, or one on somebody else's cloud, would be rendered to
        // customers.
        if (photos.Any(photo => !assets.IsTrustedSecureUrl(photo.SecureUrl)))
        {
            return CourtResult<CreatedCourtResponse>.Fail(CourtFailure.UntrustedPhotoUrl);
        }

        var now = timeProvider.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var facility = request.NewFacility is null
            ? await db.Facilities.FirstOrDefaultAsync(
                candidate => candidate.Id == request.FacilityId && candidate.FacilityOwnerId == owner.Id,
                ct)
            : null;

        if (request.NewFacility is null && facility is null)
        {
            // Scoped to the owner, so a facility belonging to somebody else is
            // answered the same way as one that does not exist.
            return CourtResult<CreatedCourtResponse>.Fail(CourtFailure.FacilityNotFound);
        }

        if (request.NewFacility is not null)
        {
            var created = await CreateFacilityAsync(owner.Id, request.NewFacility, actor, now, ct);

            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return CourtResult<CreatedCourtResponse>.Fail(created.Failure);
            }

            facility = created.Value!;
        }

        var court = new Court(
            facility!.Id,
            owner.Id,
            request.Court.Name,
            request.Court.DisplayOrder,
            request.Court.Description,
            new CourtSpace(
                request.Court.VenueType,
                request.Court.Surface,
                request.Court.HasLighting,
                request.Court.SizeLabel,
                request.Court.Capacity,
                request.Court.Equipment),
            new CourtBookingRules(
                request.Court.SlotLengthMinutes,
                request.Court.MinimumDurationMinutes,
                request.Court.BufferMinutes),
            now);
        court.SetUsesFacilityHours(request.Court.UsesFacilityHours, now);
        db.Courts.Add(court);

        foreach (var sportId in sportIds)
        {
            db.CourtSports.Add(new CourtSport(
                court.Id,
                sportId,
                sportId == request.Court.PrimarySportId,
                now));
        }

        // Written only when the court has opted out. An empty set then means
        // "follows the facility", not "nobody filled it in".
        if (!request.Court.UsesFacilityHours)
        {
            foreach (var hour in request.Court.OperatingHours)
            {
                db.CourtOperatingHours.Add(new CourtOperatingHour(
                    court.Id,
                    hour.DayOfWeek,
                    hour.OpensAt,
                    hour.ClosesAt,
                    now));
            }
        }

        PhotoGallery.Add(db, facility.Id, court.Id, request.Court.Photos, now);

        var primarySportName = await db.Sports
            .Where(sport => sport.Id == request.Court.PrimarySportId)
            .Select(sport => sport.Name)
            .FirstAsync(ct);

        audit.RecordEvent(
            actor,
            AuditAction.CourtCreated,
            AuditEntityType.Court,
            court.Id,
            new Dictionary<string, string?>
            {
                ["facilityId"] = facility.Id.ToString(),
                ["facilityName"] = facility.Name,
                ["name"] = court.Name,
                ["primarySport"] = primarySportName,
                ["sports"] = sportIds.Length.ToString(CultureInfo.InvariantCulture),
                ["venueType"] = court.VenueType,
                ["slotLengthMinutes"] = court.SlotLengthMinutes.ToString(CultureInfo.InvariantCulture),
                ["usesFacilityHours"] = court.UsesFacilityHours.ToString(),
                ["photos"] = request.Court.Photos.Count.ToString(CultureInfo.InvariantCulture)
            });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Court {CourtId} was created by {ActorUserId}.", court.Id, actor.UserId);
        return CourtResult<CreatedCourtResponse>.Success(
            new CreatedCourtResponse(court.Id, facility.Id, facility.Name));
    }

    public async Task<PagedResult<FacilityInventoryItem>> ListFacilitiesAsync(
        FacilityInventoryQuery query,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        var facilities = db.Facilities.AsNoTracking();

        if (query.FacilityOwnerId is Guid ownerId)
        {
            facilities = facilities.Where(facility => facility.FacilityOwnerId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            facilities = facilities.Where(facility =>
                EF.Functions.Like(facility.Name, $"%{search}%") ||
                EF.Functions.Like(facility.City, $"%{search}%") ||
                EF.Functions.Like(facility.FacilityOwner.BusinessName, $"%{search}%"));
        }

        var totalItems = await facilities.CountAsync(ct);

        var rows = await facilities
            .OrderBy(facility => facility.FacilityOwner.BusinessName)
            .ThenBy(facility => facility.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(facility => new
            {
                facility.Id,
                facility.Name,
                facility.Slug,
                facility.City,
                facility.Province,
                facility.FacilityOwnerId,
                facility.FacilityOwner.BusinessName,
                OwnerIsActive = facility.FacilityOwner.IsActive,
                facility.IsActive,
                facility.CreatedAt,
                CourtCount = facility.Courts.Count,
                ActiveCourtCount = facility.Courts.Count(court => court.IsActive),
                // Only the dates the status needs, rather than whole contracts.
                Terms = facility.FacilityOwner.Contracts
                    .Where(contract => contract.CancelledAt == null)
                    .Select(contract => new { contract.StartDate, contract.EndDate })
                    .ToList()
            })
            .ToListAsync(ct);

        var facilityIds = rows.Select(row => row.Id).ToArray();

        var covers = await db.Photos
            .AsNoTracking()
            .Where(photo =>
                facilityIds.Contains(photo.FacilityId) && photo.CourtId == null && photo.IsCover)
            .Select(photo => new { photo.FacilityId, photo.SecureUrl })
            .ToDictionaryAsync(photo => photo.FacilityId, photo => photo.SecureUrl, ct);

        // One read for every facility on the page, rather than one per row.
        var closures = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period =>
                facilityIds.Contains(period.FacilityId) &&
                period.CourtId == null &&
                period.LiftedAt == null &&
                period.StartsAt <= now &&
                (period.EndsAt == null || now < period.EndsAt))
            .Select(period => new
            {
                period.Id,
                period.FacilityId,
                period.Reason,
                period.StartsAt,
                period.EndsAt
            })
            .ToListAsync(ct);

        var items = rows.Select(row =>
        {
            var status = FacilityOwner.DeriveStatus(
                row.OwnerIsActive,
                row.Terms.Select(term => new ContractTerm(term.StartDate, term.EndDate)),
                today);

            var closure = closures.FirstOrDefault(period => period.FacilityId == row.Id);

            return new FacilityInventoryItem(
                row.Id,
                row.Name,
                row.Slug,
                row.City,
                row.Province,
                row.FacilityOwnerId,
                row.BusinessName,
                status.ToString(),
                row.IsActive,
                row.CourtCount,
                row.ActiveCourtCount,
                closure is null
                    ? null
                    : new MaintenanceStatus(
                        closure.Id,
                        true,
                        closure.Reason,
                        closure.StartsAt,
                        closure.EndsAt),
                covers.GetValueOrDefault(row.Id),
                // Four things have to line up. A venue missing any one of them
                // is not something a customer can book today.
                status == FacilityOwnerStatus.Commenced &&
                    row.IsActive &&
                    closure is null &&
                    row.ActiveCourtCount > 0,
                row.CreatedAt);
        });

        return new PagedResult<FacilityInventoryItem>([.. items], page, pageSize, totalItems);
    }

    public async Task<IReadOnlyCollection<CourtListItem>> ListAsync(Guid facilityId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var courts = await db.Courts
            .AsNoTracking()
            .Where(court => court.FacilityId == facilityId)
            .OrderBy(court => court.DisplayOrder)
            .ThenBy(court => court.Name)
            .Select(court => new
            {
                court.Id,
                court.FacilityId,
                FacilityName = court.Facility.Name,
                court.Name,
                court.DisplayOrder,
                court.Description,
                court.VenueType,
                court.Surface,
                court.HasLighting,
                court.SizeLabel,
                court.Capacity,
                court.Equipment,
                court.SlotLengthMinutes,
                court.MinimumDurationMinutes,
                court.BufferMinutes,
                court.UsesFacilityHours,
                court.IsActive,
                court.CreatedAt,
                Photos = db.Photos
                    .Where(photo => photo.CourtId == court.Id)
                    .OrderByDescending(photo => photo.IsCover)
                    .ThenBy(photo => photo.DisplayOrder)
                    .Select(photo => new PhotoItem(
                        photo.Id,
                        photo.PublicId,
                        photo.SecureUrl,
                        photo.Caption,
                        photo.DisplayOrder,
                        photo.IsCover))
                    .ToList(),
                Sports = court.Sports
                    .OrderByDescending(link => link.IsPrimary)
                    .ThenBy(link => link.Sport.Category)
                    .ThenBy(link => link.Sport.DisplayOrder)
                    .Select(link => new CourtSportItem(
                        link.SportId,
                        link.Sport.Key,
                        link.Sport.Name,
                        link.Sport.Category,
                        link.IsPrimary))
                    .ToList(),
                OwnHours = court.OperatingHours
                    .OrderBy(hour => hour.DayOfWeek)
                    .Select(hour => new FacilityOperatingHourDetail(
                        (int)hour.DayOfWeek,
                        hour.OpensAt,
                        hour.ClosesAt))
                    .ToList()
            })
            .ToListAsync(ct);

        if (courts.Count == 0)
        {
            return [];
        }

        // The facility's own hours, for the courts that follow them.
        var facilityHours = await db.FacilityOperatingHours
            .AsNoTracking()
            .Where(hour => hour.FacilityId == facilityId)
            .OrderBy(hour => hour.DayOfWeek)
            .Select(hour => new FacilityOperatingHourDetail(
                (int)hour.DayOfWeek,
                hour.OpensAt,
                hour.ClosesAt))
            .ToListAsync(ct);

        // One read for both levels: a period with no court named closes the
        // whole facility, and therefore every court in it.
        var closures = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period =>
                period.FacilityId == facilityId &&
                period.LiftedAt == null &&
                period.StartsAt <= now &&
                (period.EndsAt == null || now < period.EndsAt))
            .Select(period => new
            {
                period.Id,
                period.CourtId,
                period.Reason,
                period.StartsAt,
                period.EndsAt
            })
            .ToListAsync(ct);

        var facilityClosure = closures.FirstOrDefault(period => period.CourtId is null);

        return
        [
            .. courts.Select(court =>
            {
                // The facility closure wins when both apply: it is the one that
                // cannot be lifted from the court, and saying so is the point.
                var closure = facilityClosure
                    ?? closures.FirstOrDefault(period => period.CourtId == court.Id);

                return new CourtListItem(
                    court.Id,
                    court.FacilityId,
                    court.FacilityName,
                    court.Name,
                    court.DisplayOrder,
                    court.Description,
                    court.VenueType,
                    court.Surface,
                    court.HasLighting,
                    court.SizeLabel,
                    court.Capacity,
                    court.Equipment,
                    court.SlotLengthMinutes,
                    court.MinimumDurationMinutes,
                    court.BufferMinutes,
                    court.UsesFacilityHours,
                    court.IsActive,
                    court.Sports,
                    court.Photos,
                    court.UsesFacilityHours ? facilityHours : court.OwnHours,
                    closure is null
                        ? null
                        : new MaintenanceStatus(
                            closure.Id,
                            closure.CourtId is null,
                            closure.Reason,
                            closure.StartsAt,
                            closure.EndsAt),
                    court.CreatedAt);
            })
        ];
    }

    public async Task<CourtResult<Guid>> SetFacilityMaintenanceAsync(
        Guid facilityId,
        SetMaintenanceRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var facility = await db.Facilities
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == facilityId, ct);

        return facility is null
            ? CourtResult<Guid>.Fail(CourtFailure.FacilityNotFound)
            : await AddPeriodAsync(facilityId, null, facility.Name, request, actor, ct);
    }

    public async Task<CourtResult<Guid>> SetCourtMaintenanceAsync(
        Guid courtId,
        SetMaintenanceRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var court = await db.Courts
            .AsNoTracking()
            .Where(candidate => candidate.Id == courtId)
            .Select(candidate => new { candidate.Id, candidate.FacilityId, candidate.Name })
            .FirstOrDefaultAsync(ct);

        return court is null
            ? CourtResult<Guid>.Fail(CourtFailure.CourtNotFound)
            : await AddPeriodAsync(court.FacilityId, court.Id, court.Name, request, actor, ct);
    }

    public async Task<CourtResult<bool>> LiftMaintenanceAsync(
        Guid periodId,
        AuditActor actor,
        CancellationToken ct)
    {
        var period = await db.MaintenancePeriods
            .FirstOrDefaultAsync(candidate => candidate.Id == periodId, ct);

        if (period is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.MaintenanceNotFound);
        }

        var now = timeProvider.GetUtcNow();
        period.Lift(now);

        audit.RecordEvent(
            actor,
            AuditAction.MaintenanceLifted,
            period.CourtId is null ? AuditEntityType.Facility : AuditEntityType.Court,
            period.CourtId ?? period.FacilityId,
            new Dictionary<string, string?> { ["reason"] = period.Reason });

        await db.SaveChangesAsync(ct);
        return CourtResult<bool>.Success(true);
    }

    private async Task<CourtResult<Guid>> AddPeriodAsync(
        Guid facilityId,
        Guid? courtId,
        string subject,
        SetMaintenanceRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // One live closure at a time for the same subject. A second would put
        // two reasons on one badge and double any notice sent out.
        var alreadyClosed = await db.MaintenancePeriods.AnyAsync(
            period =>
                period.FacilityId == facilityId &&
                period.CourtId == courtId &&
                period.LiftedAt == null &&
                (period.EndsAt == null || period.EndsAt > now),
            ct);

        if (alreadyClosed)
        {
            return CourtResult<Guid>.Fail(CourtFailure.AlreadyUnderMaintenance);
        }

        var period = new MaintenancePeriod(
            facilityId,
            courtId,
            request.StartsAt,
            request.EndsAt,
            request.Reason,
            actor.UserId ?? Guid.Empty,
            now);
        db.MaintenancePeriods.Add(period);

        audit.RecordEvent(
            actor,
            AuditAction.MaintenanceSet,
            courtId is null ? AuditEntityType.Facility : AuditEntityType.Court,
            courtId ?? facilityId,
            new Dictionary<string, string?>
            {
                ["subject"] = subject,
                ["scope"] = courtId is null ? "Whole facility" : "This court",
                ["startsAt"] = request.StartsAt.ToString("u", CultureInfo.InvariantCulture),
                ["endsAt"] = request.EndsAt?.ToString("u", CultureInfo.InvariantCulture)
                    ?? "Until further notice"
            },
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Maintenance set on {Scope} {SubjectId} by {ActorUserId}.",
            courtId is null ? "facility" : "court",
            courtId ?? facilityId,
            actor.UserId);

        return CourtResult<Guid>.Success(period.Id);
    }

    private async Task<CourtResult<Facility>> CreateFacilityAsync(
        Guid facilityOwnerId,
        NewFacilityInput input,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!IsKnownTimeZone(input.Details.TimeZone))
        {
            return CourtResult<Facility>.Fail(CourtFailure.UnknownTimeZone);
        }

        var amenityIds = input.Details.AmenityIds.Distinct().ToArray();
        if (amenityIds.Length > 0)
        {
            var known = await db.Amenities
                .CountAsync(amenity => amenityIds.Contains(amenity.Id) && amenity.IsActive, ct);
            if (known != amenityIds.Length)
            {
                return CourtResult<Facility>.Fail(CourtFailure.UnknownAmenity);
            }
        }

        var facility = new Facility(
            facilityOwnerId,
            input.Details.Name,
            await FacilitySlugs.ReserveAsync(db, input.Details.Name, ct),
            input.Details.Description,
            new FacilityAddress(
                input.Details.AddressLine1,
                input.Details.AddressLine2,
                input.Details.City,
                input.Details.Province,
                input.Details.PostalCode,
                input.Details.Country),
            new FacilityContact(input.Details.ContactPhone, input.Details.ContactEmail),
            new FacilityPolicies(input.Details.SafetyMeasures, input.Details.HouseRules),
            input.Details.TimeZone,
            now);
        facility.SetCoordinates(input.Details.Latitude, input.Details.Longitude, now);
        db.Facilities.Add(facility);

        foreach (var hour in input.OperatingHours)
        {
            db.FacilityOperatingHours.Add(new FacilityOperatingHour(
                facility.Id,
                hour.DayOfWeek,
                hour.OpensAt,
                hour.ClosesAt,
                now));
        }

        foreach (var amenityId in amenityIds)
        {
            db.FacilityAmenities.Add(new FacilityAmenity(facility.Id, amenityId, now));
        }

        PhotoGallery.Add(db, facility.Id, null, input.Photos, now);

        audit.RecordEvent(
            actor,
            AuditAction.FacilityCreated,
            AuditEntityType.Facility,
            facility.Id,
            new Dictionary<string, string?>
            {
                ["name"] = facility.Name,
                ["slug"] = facility.Slug,
                ["city"] = facility.City
            });

        return CourtResult<Facility>.Success(facility);
    }

    private static bool IsKnownTimeZone(string timeZone)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
