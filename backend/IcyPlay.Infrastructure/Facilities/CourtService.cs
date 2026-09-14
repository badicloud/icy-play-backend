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
    IActivityCatalog catalog,
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

        var courtSports = request.Court.Sports
            .GroupBy(sport => sport.SportId)
            .Select(group => group.First())
            .ToArray();
        var sportIds = courtSports.Select(sport => sport.SportId).ToArray();

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

        var links = new List<CourtSport>();

        foreach (var sport in courtSports)
        {
            var link = new CourtSport(
                court.Id,
                sport.SportId,
                sport.SportId == request.Court.PrimarySportId,
                sport.Divisions,
                now);
            db.CourtSports.Add(link);
            links.Add(link);
        }

        // What the court actually sells, written in the same transaction as the
        // court. A court that exists without its bookable courts is invisible to
        // customers, and the gap would be measured in however long it took
        // somebody to notice.
        BookableCourtRoster.Reconcile(db, court.Id, links, now);

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

        catalog.Invalidate();
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

    public async Task<PagedResult<CourtInventoryItem>> ListInventoryAsync(
        CourtInventoryQuery query,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 20 : query.PageSize, 1, 100);

        var courts = db.Courts.AsNoTracking();

        if (query.FacilityOwnerId is Guid ownerId)
        {
            courts = courts.Where(court => court.FacilityOwnerId == ownerId);
        }

        if (query.FacilityId is Guid facilityId)
        {
            courts = courts.Where(court => court.FacilityId == facilityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            // The three names an admin would actually type: the court, the
            // venue it is in, or the business that owns it.
            courts = courts.Where(court =>
                EF.Functions.Like(court.Name, $"%{search}%") ||
                EF.Functions.Like(court.Facility.Name, $"%{search}%") ||
                EF.Functions.Like(court.Facility.FacilityOwner.BusinessName, $"%{search}%"));
        }

        var totalItems = await courts.CountAsync(ct);

        var rows = await courts
            .OrderBy(court => court.Facility.Name)
            .ThenBy(court => court.DisplayOrder)
            .ThenBy(court => court.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(court => new
            {
                court.Id,
                court.Name,
                court.DisplayOrder,
                court.IsActive,
                court.FacilityId,
                FacilityName = court.Facility.Name,
                court.FacilityOwnerId,
                BusinessName = court.Facility.FacilityOwner.BusinessName,
                court.Facility.City,
                court.Facility.Province,
                court.VenueType,
                CoverPhotoUrl = db.Photos
                    .Where(photo => photo.CourtId == court.Id && photo.IsCover)
                    .Select(photo => photo.SecureUrl)
                    .FirstOrDefault(),
                Sports = court.Sports
                    .OrderByDescending(link => link.IsPrimary)
                    .ThenBy(link => link.Sport.Name)
                    .Select(link => new CourtSportItem(
                        link.SportId,
                        link.Sport.Key,
                        link.Sport.Name,
                        link.Sport.Category,
                        link.Sport.Kind,
                        link.IsPrimary,
                        link.Divisions,
                        link.StandardHourlyRate,
                        link.PeakHourlyRate,
                        link.WeekendRate,
                        link.HolidayRate))
                    .ToList(),
                // A court marked out three ways for one sport is three things to
                // book, and saying "one court" would undersell the venue.
                // Read rather than counted, so the number here, the number on the
                // court page and the number of cards a customer sees are one
                // number with one source.
                BookableUnits = court.Sports
                    .Sum(link => link.BookableCourts.Count(unit => unit.IsActive)),
                Closure = db.MaintenancePeriods
                    .Where(period =>
                        period.LiftedAt == null &&
                        period.StartsAt <= now &&
                        (period.EndsAt == null || now < period.EndsAt) &&
                        (period.CourtId == court.Id ||
                            (period.CourtId == null && period.FacilityId == court.FacilityId)))
                    // The facility closure wins when both apply: it is the one
                    // that cannot be lifted from the court.
                    .OrderBy(period => period.CourtId == null ? 0 : 1)
                    .Select(period => new MaintenanceStatus(
                        period.Id,
                        period.CourtId == null,
                        period.Reason,
                        period.StartsAt,
                        period.EndsAt))
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return new PagedResult<CourtInventoryItem>(
            [
                .. rows.Select(row => new CourtInventoryItem(
                    row.Id,
                    row.Name,
                    row.DisplayOrder,
                    row.IsActive,
                    row.FacilityId,
                    row.FacilityName,
                    row.FacilityOwnerId,
                    row.BusinessName,
                    row.City,
                    row.Province,
                    row.VenueType,
                    row.CoverPhotoUrl,
                    row.Sports,
                    row.BookableUnits,
                    row.Closure))
            ],
            page,
            pageSize,
            totalItems);
    }

    public async Task<CourtListItem?> GetAsync(Guid courtId, CancellationToken ct)
    {
        var facilityId = await db.Courts
            .AsNoTracking()
            .Where(court => court.Id == courtId)
            .Select(court => (Guid?)court.FacilityId)
            .SingleOrDefaultAsync(ct);

        if (facilityId is null)
        {
            return null;
        }

        // Read through the list rather than repeating its hour resolution and
        // its two levels of maintenance. A facility holds a handful of courts,
        // and a detail page that can disagree with the list it came from is a
        // worse problem than a few extra rows read.
        var courts = await ListAsync(facilityId.Value, ct);
        return courts.SingleOrDefault(court => court.Id == courtId);
    }

    public async Task<CourtResult<bool>> UpdateAsync(
        Guid courtId,
        UpdateCourtRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var court = await db.Courts
            .Include(candidate => candidate.Sports)
                .ThenInclude(link => link.BookableCourts)
            .Include(candidate => candidate.OperatingHours)
            .SingleOrDefaultAsync(candidate => candidate.Id == courtId, ct);

        if (court is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.CourtNotFound);
        }

        var input = request.Court;
        var courtSports = input.Sports
            .GroupBy(sport => sport.SportId)
            .Select(group => group.First())
            .ToArray();
        var sportIds = courtSports.Select(sport => sport.SportId).ToArray();

        if (!sportIds.Contains(input.PrimarySportId))
        {
            return CourtResult<bool>.Fail(CourtFailure.PrimarySportNotSelected);
        }

        var knownSports = await db.Sports
            .CountAsync(sport => sportIds.Contains(sport.Id) && sport.IsActive, ct);
        if (knownSports != sportIds.Length)
        {
            return CourtResult<bool>.Fail(CourtFailure.UnknownSport);
        }

        // Posted by the browser, so never taken on trust.
        if (input.Photos.Any(photo => !assets.IsTrustedSecureUrl(photo.SecureUrl)))
        {
            return CourtResult<bool>.Fail(CourtFailure.UntrustedPhotoUrl);
        }

        if (input.Photos.Count(photo => photo.IsCover) > 1)
        {
            return CourtResult<bool>.Fail(CourtFailure.TooManyCovers);
        }

        var now = timeProvider.GetUtcNow();
        var before = CourtSnapshot(court);

        court.UpdateDetails(
            input.Name,
            input.DisplayOrder,
            input.Description,
            new CourtSpace(
                input.VenueType,
                input.Surface,
                input.HasLighting,
                input.SizeLabel,
                input.Capacity,
                input.Equipment),
            new CourtBookingRules(
                input.SlotLengthMinutes,
                input.MinimumDurationMinutes,
                input.BufferMinutes),
            now);

        if (request.IsActive != court.IsActive)
        {
            if (request.IsActive)
            {
                court.Reactivate(now);
            }
            else
            {
                court.Deactivate(now);
            }
        }

        audit.RecordChange(
            actor,
            AuditAction.CourtUpdated,
            AuditEntityType.Court,
            court.Id,
            before,
            CourtSnapshot(court),
            request.Reason);

        var sportsBefore = SportSnapshot(court);
        ReplaceSports(court, courtSports, input.PrimarySportId, now);
        audit.RecordChange(
            actor,
            AuditAction.CourtSportsUpdated,
            AuditEntityType.Court,
            court.Id,
            sportsBefore,
            SportSnapshot(court),
            request.Reason);

        var hoursBefore = HoursSnapshot(court);
        ApplyOwnHours(court, input, now);
        audit.RecordChange(
            actor,
            AuditAction.CourtHoursUpdated,
            AuditEntityType.Court,
            court.Id,
            hoursBefore,
            HoursSnapshot(court),
            request.Reason);

        BookableCourtRoster.Reconcile(db, court.Id, court.Sports, now);

        var photosBefore = await PhotoSnapshotAsync(court.Id, ct);
        await PhotoGallery.ReplaceAsync(db, court.FacilityId, court.Id, input.Photos, now, ct);
        audit.RecordChange(
            actor,
            AuditAction.CourtPhotosUpdated,
            AuditEntityType.Court,
            court.Id,
            photosBefore,
            PhotoGallery.Snapshot(input.Photos),
            request.Reason);

        await db.SaveChangesAsync(ct);
        catalog.Invalidate();
        logger.LogInformation("Court {CourtId} was updated by {ActorUserId}.", court.Id, actor.UserId);
        return CourtResult<bool>.Success(true);
    }

    public async Task<CourtResult<bool>> UpdateDivisionsAsync(
        Guid courtId,
        UpdateCourtDivisionsRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var court = await db.Courts
            .Include(candidate => candidate.Sports)
                .ThenInclude(link => link.BookableCourts)
            .SingleOrDefaultAsync(candidate => candidate.Id == courtId, ct);

        if (court is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.CourtNotFound);
        }

        // Dividing a sport this court does not take would sit in the table
        // unreachable, and quietly become wrong when the sport is added later.
        if (request.Sports.Any(input => court.Sports.All(link => link.SportId != input.SportId)))
        {
            return CourtResult<bool>.Fail(CourtFailure.UnknownSport);
        }

        var now = timeProvider.GetUtcNow();
        var before = SportSnapshot(court);

        foreach (var input in request.Sports)
        {
            court.Sports
                .Single(candidate => candidate.SportId == input.SportId)
                .SetDivisions(input.Divisions, now);
        }

        BookableCourtRoster.Reconcile(db, court.Id, court.Sports, now);

        audit.RecordChange(
            actor,
            AuditAction.CourtDivisionsUpdated,
            AuditEntityType.Court,
            court.Id,
            before,
            SportSnapshot(court),
            request.Reason);

        await db.SaveChangesAsync(ct);
        catalog.Invalidate();
        logger.LogInformation(
            "Divisions on court {CourtId} were updated by {ActorUserId}.",
            court.Id,
            actor.UserId);
        return CourtResult<bool>.Success(true);
    }

    public async Task<CourtResult<bool>> UpdatePricingAsync(
        Guid courtId,
        UpdateCourtPricingRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var court = await db.Courts
            .Include(candidate => candidate.Sports)
            .Include(candidate => candidate.OperatingHours)
            .SingleOrDefaultAsync(candidate => candidate.Id == courtId, ct);

        if (court is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.CourtNotFound);
        }

        // A price for a sport this court does not take would sit in the table
        // unreachable, and quietly become wrong when the sport is added later.
        if (request.Sports.Any(input => court.Sports.All(link => link.SportId != input.SportId)))
        {
            return CourtResult<bool>.Fail(CourtFailure.UnknownSport);
        }

        if (request.PeakWindow is { StartsAt: not null, EndsAt: not null } proposed)
        {
            var fit = await PeakWindowFitsHoursAsync(court, proposed, ct);
            if (fit != CourtFailure.None)
            {
                return CourtResult<bool>.Fail(fit);
            }
        }

        var now = timeProvider.GetUtcNow();
        var before = PricingSnapshot(court);
        var beforeWindow = PeakWindowSnapshot(court);

        foreach (var input in request.Sports)
        {
            var link = court.Sports.Single(candidate => candidate.SportId == input.SportId);
            link.SetPricing(
                input.StandardHourlyRate,
                input.PeakHourlyRate,
                input.WeekendRate,
                input.HolidayRate,
                now);
        }

        if (request.PeakWindow is PeakWindowInput window)
        {
            court.SetPeakWindow(window.StartsAt, window.EndsAt, window.OnWeekdays, window.OnWeekends, now);
        }

        // A window nobody is charged for is clutter that will read as a live
        // rule the next time someone opens the screen.
        if (court.Sports.All(link => link.PeakHourlyRate is null))
        {
            court.SetPeakWindow(null, null, false, false, now);
        }

        audit.RecordChange(
            actor,
            AuditAction.CourtPricingUpdated,
            AuditEntityType.Court,
            court.Id,
            Merge(before, beforeWindow),
            Merge(PricingSnapshot(court), PeakWindowSnapshot(court)),
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Pricing on court {CourtId} was updated by {ActorUserId}.",
            court.Id,
            actor.UserId);
        return CourtResult<bool>.Success(true);
    }

    private static Dictionary<string, string?> Merge(
        Dictionary<string, string?> first,
        Dictionary<string, string?> second)
    {
        var merged = new Dictionary<string, string?>(first);

        foreach (var pair in second)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    /// <summary>
    /// One line per sport, so the audit diff names the sport whose price moved
    /// rather than reporting that "pricing" changed.
    /// </summary>
    private static Dictionary<string, string?> PricingSnapshot(Court court) =>
        court.Sports
            .OrderBy(link => link.SportId)
            .ToDictionary(
                link => link.SportId.ToString(),
                link => link.StandardHourlyRate is null
                    ? null
                    : string.Join(
                        "/",
                        Money(link.StandardHourlyRate),
                        Money(link.PeakHourlyRate),
                        Money(link.WeekendRate),
                        Money(link.HolidayRate)));

    /// <summary>
    /// Whether the proposed peak window sits inside the hours this court is
    /// actually open on the days it applies to. A window running past closing
    /// time prices hours nobody can book, which reads as a rule and behaves as
    /// nothing.
    /// </summary>
    private async Task<CourtFailure> PeakWindowFitsHoursAsync(
        Court court,
        PeakWindowInput window,
        CancellationToken ct)
    {
        var hours = court.UsesFacilityHours
            ? await db.FacilityOperatingHours
                .AsNoTracking()
                .Where(hour => hour.FacilityId == court.FacilityId)
                .Select(hour => new { hour.DayOfWeek, hour.OpensAt, hour.ClosesAt })
                .ToArrayAsync(ct)
            : [.. court.OperatingHours.Select(hour => new { hour.DayOfWeek, hour.OpensAt, hour.ClosesAt })];

        var applicable = hours
            .Where(hour =>
                Court.IsWeekend(hour.DayOfWeek) ? window.OnWeekends : window.OnWeekdays)
            .Where(hour => hour.OpensAt is not null && hour.ClosesAt is not null)
            .ToArray();

        if (applicable.Length == 0)
        {
            return CourtFailure.PeakWindowOnClosedDays;
        }

        // Measured against the narrowest of the days it covers: a window that
        // fits Monday but overruns an early Saturday close is wrong on the
        // Saturday, and saying so is more use than silently applying it.
        var latestOpen = applicable.Max(hour => hour.OpensAt!.Value);
        var earliestClose = applicable.Min(hour => hour.ClosesAt!.Value);

        return window.StartsAt >= latestOpen && window.EndsAt <= earliestClose
            ? CourtFailure.None
            : CourtFailure.PeakWindowOutsideHours;
    }

    private static Dictionary<string, string?> PeakWindowSnapshot(Court court) =>
        new()
        {
            ["peakWindow"] = court.HasPeakWindow
                ? FormattableString.Invariant($"{court.PeakStartsAt}-{court.PeakEndsAt}")
                : null,
            ["peakDays"] = court.HasPeakWindow
                ? string.Join(
                    " and ",
                    new[]
                    {
                        court.PeakOnWeekdays ? "weekdays" : null,
                        court.PeakOnWeekends ? "weekends" : null
                    }.Where(part => part is not null))
                : null
        };

    private static string Money(decimal? amount) =>
        amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-";

    /// <summary>
    /// Keeps the links that survive rather than deleting and re-adding them:
    /// the row carries its own id, and churning it would throw away when a
    /// sport was first put on this court.
    /// </summary>
    private void ReplaceSports(
        Court court,
        IReadOnlyCollection<CourtSportInput> sports,
        Guid primarySportId,
        DateTimeOffset now)
    {
        var sportIds = sports.Select(sport => sport.SportId).ToArray();

        foreach (var dropped in court.Sports.Where(link => !sportIds.Contains(link.SportId)).ToArray())
        {
            db.CourtSports.Remove(dropped);
            court.Sports.Remove(dropped);
        }

        foreach (var sport in sports)
        {
            var existing = court.Sports.FirstOrDefault(link => link.SportId == sport.SportId);

            if (existing is null)
            {
                // Through the set, because a client-generated key added only to
                // a tracked navigation is read by EF as a row that already exists.
                db.CourtSports.Add(new CourtSport(
                    court.Id,
                    sport.SportId,
                    sport.SportId == primarySportId,
                    sport.Divisions,
                    now));
            }
            else
            {
                existing.SetPrimary(sport.SportId == primarySportId, now);
                existing.SetDivisions(sport.Divisions, now);
            }
        }
    }

    /// <summary>
    /// Hours are set in place. A court keeps one row per day under a unique
    /// index, so deleting and re-inserting them collides with itself inside the
    /// same transaction.
    /// </summary>
    private void ApplyOwnHours(Court court, CourtInput input, DateTimeOffset now)
    {
        court.SetUsesFacilityHours(input.UsesFacilityHours, now);

        if (input.UsesFacilityHours)
        {
            // The rows are left where they are: a court that goes back to
            // keeping its own hours should find them as it left them.
            return;
        }

        foreach (var hour in input.OperatingHours)
        {
            var existing = court.OperatingHours.FirstOrDefault(row => row.DayOfWeek == hour.DayOfWeek);

            if (existing is null)
            {
                db.CourtOperatingHours.Add(new CourtOperatingHour(
                    court.Id,
                    hour.DayOfWeek,
                    hour.OpensAt,
                    hour.ClosesAt,
                    now));
            }
            else
            {
                existing.SetHours(hour.OpensAt, hour.ClosesAt, now);
            }
        }
    }

    private static Dictionary<string, string?> CourtSnapshot(Court court) =>
        new()
        {
            ["name"] = court.Name,
            ["displayOrder"] = court.DisplayOrder.ToString(CultureInfo.InvariantCulture),
            ["description"] = court.Description,
            ["venueType"] = court.VenueType,
            ["surface"] = court.Surface,
            ["hasLighting"] = court.HasLighting.ToString(),
            ["sizeLabel"] = court.SizeLabel,
            ["capacity"] = court.Capacity?.ToString(CultureInfo.InvariantCulture),
            ["equipment"] = court.Equipment,
            ["slotLengthMinutes"] = court.SlotLengthMinutes.ToString(CultureInfo.InvariantCulture),
            ["minimumDurationMinutes"] = court.MinimumDurationMinutes.ToString(CultureInfo.InvariantCulture),
            ["bufferMinutes"] = court.BufferMinutes.ToString(CultureInfo.InvariantCulture),
            ["isActive"] = court.IsActive.ToString()
        };

    private static Dictionary<string, string?> SportSnapshot(Court court) =>
        new()
        {
            ["sportIds"] = string.Join(
                ",",
                court.Sports.Select(link => link.SportId.ToString()).OrderBy(id => id)),
            ["primarySportId"] = court.Sports
                .FirstOrDefault(link => link.IsPrimary)?.SportId.ToString(),
            ["divisions"] = string.Join(
                ",",
                court.Sports
                    .OrderBy(link => link.SportId)
                    .Select(link => FormattableString.Invariant($"{link.SportId}:{link.Divisions}")))
        };

    private static Dictionary<string, string?> HoursSnapshot(Court court) =>
        new()
        {
            ["usesFacilityHours"] = court.UsesFacilityHours.ToString(),
            ["hours"] = court.UsesFacilityHours
                ? null
                : string.Join(
                    ",",
                    court.OperatingHours
                        .OrderBy(hour => hour.DayOfWeek)
                        .Select(hour => hour.IsClosed
                            ? FormattableString.Invariant($"{hour.DayOfWeek}:closed")
                            : FormattableString.Invariant(
                                $"{hour.DayOfWeek}:{hour.OpensAt}-{hour.ClosesAt}")))
        };

    private async Task<Dictionary<string, string?>> PhotoSnapshotAsync(Guid courtId, CancellationToken ct)
    {
        var photos = await db.Photos
            .AsNoTracking()
            .Where(photo => photo.CourtId == courtId)
            .Select(photo => new { photo.PublicId, photo.IsCover })
            .ToListAsync(ct);

        return new Dictionary<string, string?>
        {
            ["photos"] = photos.Count.ToString(CultureInfo.InvariantCulture),
            ["cover"] = photos.FirstOrDefault(photo => photo.IsCover)?.PublicId
        };
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
                court.FacilityOwnerId,
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
                court.PeakStartsAt,
                court.PeakEndsAt,
                court.PeakOnWeekdays,
                court.PeakOnWeekends,
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
                        link.Sport.Kind,
                        link.IsPrimary,
                        link.Divisions,
                        link.StandardHourlyRate,
                        link.PeakHourlyRate,
                        link.WeekendRate,
                        link.HolidayRate))
                    .ToList(),
                OwnHours = court.OperatingHours
                    .OrderBy(hour => hour.DayOfWeek)
                    .Select(hour => new FacilityOperatingHourDetail(
                        (int)hour.DayOfWeek,
                        hour.OpensAt,
                        hour.ClosesAt))
                    .ToList(),
                // Read rather than counted out, so the console and the public
                // listing are looking at the same rows.
                BookableCourts = court.Sports
                    .SelectMany(link => link.BookableCourts
                        .Where(unit => unit.IsActive)
                        .Select(unit => new
                        {
                            unit.Id,
                            link.SportId,
                            SportName = link.Sport.Name,
                            unit.DivisionNumber,
                            unit.Kind,
                            link.Divisions
                        }))
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
                    court.FacilityOwnerId,
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
                    court.PeakStartsAt,
                    court.PeakEndsAt,
                    court.PeakOnWeekdays,
                    court.PeakOnWeekends,
                    court.Sports,
                    [
                        .. court.BookableCourts
                            .OrderBy(unit => unit.SportName, StringComparer.Ordinal)
                            .ThenBy(unit => unit.DivisionNumber)
                            .Select(unit => new BookableCourtItem(
                                unit.Id,
                                unit.SportId,
                                unit.SportName,
                                unit.DivisionNumber,
                                Court.DivisionName(
                                    court.Name,
                                    unit.SportName,
                                    unit.DivisionNumber,
                                    unit.Divisions),
                                unit.Kind))
                    ],
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
        catalog.Invalidate();
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
        catalog.Invalidate();
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
