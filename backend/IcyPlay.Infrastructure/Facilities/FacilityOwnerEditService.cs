using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Facilities;

public sealed class FacilityOwnerEditService(
    AppDbContext db,
    IAuditLogger audit,
    ICloudinaryAssetService assets,
    TimeProvider timeProvider,
    ILogger<FacilityOwnerEditService> logger) : IFacilityOwnerEditService
{
    public async Task<EditResult> UpdateBusinessAsync(
        Guid facilityOwnerId,
        UpdateBusinessRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .SingleOrDefaultAsync(candidate => candidate.Id == facilityOwnerId, ct);

        if (owner is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        var before = BusinessSnapshot(owner);
        owner.UpdateBusinessDetails(
            request.BusinessName,
            request.BillingEmail,
            request.BillingPhone,
            request.BusinessRegistrationNumber,
            timeProvider.GetUtcNow());

        audit.RecordChange(
            actor,
            AuditAction.FacilityOwnerBusinessUpdated,
            AuditEntityType.FacilityOwner,
            owner.Id,
            before,
            BusinessSnapshot(owner),
            request.Reason);

        await db.SaveChangesAsync(ct);
        return EditResult.Success();
    }

    public async Task<EditResult> UpdateFacilityAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        UpdateFacilityRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        if (!IsKnownTimeZone(request.TimeZone))
        {
            return EditResult.Fail(EditFailure.UnknownTimeZone);
        }

        // Scoped to the owner in the same query. Another owner's facility is
        // answered the same way as a missing one, so the endpoint cannot be used
        // to find out which ids exist.
        var facility = await db.Facilities
            .Include(candidate => candidate.Amenities)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == facilityId && candidate.FacilityOwnerId == facilityOwnerId,
                ct);

        if (facility is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        var amenityIds = request.AmenityIds.Distinct().ToArray();
        if (amenityIds.Length > 0)
        {
            var known = await db.Amenities
                .CountAsync(amenity => amenityIds.Contains(amenity.Id) && amenity.IsActive, ct);
            if (known != amenityIds.Length)
            {
                return EditResult.Fail(EditFailure.UnknownAmenity);
            }
        }

        var now = timeProvider.GetUtcNow();
        var before = FacilitySnapshot(facility);
        var amenitiesBefore = AmenitySnapshot(facility);

        // The slug deliberately does not follow the name. It is the public web
        // address, and a rename must not quietly break every link already
        // shared for this venue.
        facility.UpdateDetails(
            request.Name,
            request.Description,
            new FacilityAddress(
                request.AddressLine1,
                request.AddressLine2,
                request.City,
                request.Province,
                request.PostalCode,
                request.Country),
            new FacilityContact(request.ContactPhone, request.ContactEmail),
            new FacilityPolicies(request.SafetyMeasures, request.HouseRules),
            request.TimeZone,
            now);
        facility.SetCoordinates(request.Latitude, request.Longitude, now);

        audit.RecordChange(
            actor,
            AuditAction.FacilityUpdated,
            AuditEntityType.Facility,
            facility.Id,
            before,
            FacilitySnapshot(facility),
            request.Reason);

        ReplaceAmenities(facility, amenityIds, now);

        audit.RecordChange(
            actor,
            AuditAction.FacilityAmenitiesUpdated,
            AuditEntityType.Facility,
            facility.Id,
            amenitiesBefore,
            AmenitySnapshot(facility),
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Facility {FacilityId} was updated by {ActorUserId}.", facility.Id, actor.UserId);
        return EditResult.Success();
    }

    public async Task<EditResult> UpdateOperatingHoursAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        UpdateOperatingHoursRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var facility = await db.Facilities
            .Include(candidate => candidate.OperatingHours)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == facilityId && candidate.FacilityOwnerId == facilityOwnerId,
                ct);

        if (facility is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        var before = HoursSnapshot(facility);

        // Updated in place rather than deleted and reinserted. A delete and an
        // insert on the same (facility, day) pair inside one save collide on
        // the unique index that stops a facility holding two answers for
        // Monday, and the rows keep their identity this way.
        foreach (var input in request.OperatingHours)
        {
            var existing = facility.OperatingHours
                .SingleOrDefault(hour => hour.DayOfWeek == input.DayOfWeek);

            if (existing is null)
            {
                var added = new FacilityOperatingHour(
                    facility.Id,
                    input.DayOfWeek,
                    input.OpensAt,
                    input.ClosesAt,
                    now);

                // Added through the set, not only the navigation. These
                // entities carry a client-generated key, and EF reads a key
                // that is already set as "this row exists" — so adding it to a
                // tracked collection alone marks it Modified and sends an
                // UPDATE for a row that was never inserted.
                db.FacilityOperatingHours.Add(added);
                facility.OperatingHours.Add(added);
            }
            else
            {
                existing.SetHours(input.OpensAt, input.ClosesAt, now);
            }
        }

        // A day the caller left out is a day the facility no longer describes.
        var supplied = request.OperatingHours.Select(hour => hour.DayOfWeek).ToHashSet();
        foreach (var dropped in facility.OperatingHours.Where(hour => !supplied.Contains(hour.DayOfWeek)).ToList())
        {
            db.FacilityOperatingHours.Remove(dropped);
            facility.OperatingHours.Remove(dropped);
        }

        audit.RecordChange(
            actor,
            AuditAction.FacilityHoursUpdated,
            AuditEntityType.Facility,
            facility.Id,
            before,
            HoursSnapshot(facility),
            request.Reason);

        await db.SaveChangesAsync(ct);
        return EditResult.Success();
    }

    public async Task<EditResult> RenewContractAsync(
        Guid facilityOwnerId,
        RenewContractRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .Include(candidate => candidate.Contracts)
            .SingleOrDefaultAsync(candidate => candidate.Id == facilityOwnerId, ct);

        if (owner is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        // Two live terms covering the same day cannot both be the one platform
        // fees are calculated against, so the overlap is refused rather than
        // silently picked between later.
        var overlaps = owner.Contracts.Any(contract =>
            contract.CancelledAt is null &&
            contract.StartDate <= request.EndDate &&
            request.StartDate <= contract.EndDate);

        if (overlaps)
        {
            return EditResult.Fail(EditFailure.OverlappingContract);
        }

        // The browser posts this back, so it is checked before it is stored.
        if (!assets.IsTrustedSecureUrl(request.Document?.SecureUrl))
        {
            return EditResult.Fail(EditFailure.UntrustedContractDocument);
        }

        var now = timeProvider.GetUtcNow();
        var contract = new FacilityOwnerContract(
            owner.Id,
            request.StartDate,
            request.EndDate,
            actor.UserId ?? Guid.Empty,
            request.Notes,
            now);
        contract.AttachDocument(
            request.Document.PublicId,
            request.Document.SecureUrl,
            request.Document.FileName,
            request.Document.ContentType,
            request.Document.SizeInBytes,
            now);
        db.FacilityOwnerContracts.Add(contract);

        audit.RecordEvent(
            actor,
            AuditAction.ContractCommenced,
            AuditEntityType.FacilityOwnerContract,
            contract.Id,
            new Dictionary<string, string?>
            {
                ["facilityOwnerId"] = owner.Id.ToString(),
                ["startDate"] = request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = request.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["notes"] = request.Notes,
                ["agreement"] = request.Document.FileName
            },
            request.Reason);

        await db.SaveChangesAsync(ct);
        return EditResult.Success();
    }

    public async Task<EditResult> ReplaceContractDocumentAsync(
        Guid facilityOwnerId,
        Guid contractId,
        ReplaceContractDocumentRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        if (!assets.IsTrustedSecureUrl(request.Document?.SecureUrl))
        {
            return EditResult.Fail(EditFailure.UntrustedContractDocument);
        }

        var contract = await db.FacilityOwnerContracts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == contractId && candidate.FacilityOwnerId == facilityOwnerId,
                ct);

        if (contract is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        var before = new Dictionary<string, string?>
        {
            ["agreement"] = contract.DocumentFileName
        };

        contract.AttachDocument(
            request.Document.PublicId,
            request.Document.SecureUrl,
            request.Document.FileName,
            request.Document.ContentType,
            request.Document.SizeInBytes,
            timeProvider.GetUtcNow());

        audit.RecordChange(
            actor,
            AuditAction.ContractDocumentReplaced,
            AuditEntityType.FacilityOwnerContract,
            contract.Id,
            before,
            new Dictionary<string, string?> { ["agreement"] = contract.DocumentFileName },
            request.Reason);

        await db.SaveChangesAsync(ct);
        return EditResult.Success();
    }

    public async Task<EditResult> CancelContractAsync(
        Guid facilityOwnerId,
        Guid contractId,
        CancelContractRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var contract = await db.FacilityOwnerContracts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == contractId && candidate.FacilityOwnerId == facilityOwnerId,
                ct);

        if (contract is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        if (contract.CancelledAt is not null)
        {
            return EditResult.Fail(EditFailure.AlreadyCancelled);
        }

        var now = timeProvider.GetUtcNow();
        contract.Cancel(now);

        audit.RecordEvent(
            actor,
            AuditAction.ContractCancelled,
            AuditEntityType.FacilityOwnerContract,
            contract.Id,
            new Dictionary<string, string?>
            {
                ["facilityOwnerId"] = facilityOwnerId.ToString(),
                ["startDate"] = contract.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = contract.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Contract {ContractId} was cancelled by {ActorUserId}.", contractId, actor.UserId);
        return EditResult.Success();
    }

    public async Task<IReadOnlyCollection<ActivityEntry>> ListActivityAsync(
        Guid facilityOwnerId,
        CancellationToken ct)
    {
        // Everything about this owner, whichever record it was written against:
        // the owner itself, their facilities, and their contracts.
        var facilityIds = await db.Facilities
            .Where(facility => facility.FacilityOwnerId == facilityOwnerId)
            .Select(facility => facility.Id)
            .ToArrayAsync(ct);

        var contractIds = await db.FacilityOwnerContracts
            .Where(contract => contract.FacilityOwnerId == facilityOwnerId)
            .Select(contract => contract.Id)
            .ToArrayAsync(ct);

        var entries = await db.AuditLogs
            .AsNoTracking()
            .Where(entry =>
                (entry.EntityType == AuditEntityType.FacilityOwner && entry.EntityId == facilityOwnerId) ||
                (entry.EntityType == AuditEntityType.Facility && facilityIds.Contains(entry.EntityId)) ||
                (entry.EntityType == AuditEntityType.FacilityOwnerContract &&
                    contractIds.Contains(entry.EntityId)))
            .OrderByDescending(entry => entry.CreatedAt)
            .Select(entry => new
            {
                entry.Id,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.ActorUserId,
                entry.ActorRole,
                entry.OldValuesJson,
                entry.NewValuesJson,
                entry.Reason,
                entry.CreatedAt
            })
            .ToListAsync(ct);

        var actorIds = entries
            .Where(entry => entry.ActorUserId is not null)
            .Select(entry => entry.ActorUserId!.Value)
            .Distinct()
            .ToArray();

        var actorNames = await db.Users
            .AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName })
            .ToDictionaryAsync(user => user.Id, user => user.FullName, ct);

        return
        [
            .. entries.Select(entry => new ActivityEntry(
                entry.Id,
                entry.Action,
                entry.EntityType,
                entry.EntityId,
                entry.ActorUserId,
                entry.ActorUserId is null ? null : actorNames.GetValueOrDefault(entry.ActorUserId.Value),
                entry.ActorRole,
                entry.OldValuesJson,
                entry.NewValuesJson,
                entry.Reason,
                entry.CreatedAt))
        ];
    }

    /// <summary>
    /// Rows are added and removed through the set, with the navigation kept in
    /// step so the audit snapshot taken afterwards is truthful. Going through
    /// the navigation alone is not enough: these entities carry a
    /// client-generated key, and EF reads an already-set key as "this row
    /// exists", marking the entity Modified and issuing an UPDATE for a row
    /// that was never inserted.
    /// </summary>
    private void ReplaceAmenities(Facility facility, IReadOnlyCollection<Guid> amenityIds, DateTimeOffset now)
    {
        var current = facility.Amenities.ToList();

        foreach (var link in current.Where(link => !amenityIds.Contains(link.AmenityId)))
        {
            db.FacilityAmenities.Remove(link);
            facility.Amenities.Remove(link);
        }

        foreach (var amenityId in amenityIds.Where(id => current.All(link => link.AmenityId != id)))
        {
            var added = new FacilityAmenity(facility.Id, amenityId, now);
            db.FacilityAmenities.Add(added);
            facility.Amenities.Add(added);
        }
    }

    private static Dictionary<string, string?> BusinessSnapshot(Domain.Identity.FacilityOwner owner) =>
        new()
        {
            ["businessName"] = owner.BusinessName,
            ["billingEmail"] = owner.BillingEmail,
            ["billingPhone"] = owner.BillingPhone,
            ["businessRegistrationNumber"] = owner.BusinessRegistrationNumber
        };

    private static Dictionary<string, string?> FacilitySnapshot(Facility facility) =>
        new()
        {
            ["name"] = facility.Name,
            ["description"] = facility.Description,
            ["addressLine1"] = facility.AddressLine1,
            ["addressLine2"] = facility.AddressLine2,
            ["city"] = facility.City,
            ["province"] = facility.Province,
            ["postalCode"] = facility.PostalCode,
            ["country"] = facility.Country,
            ["latitude"] = facility.Latitude?.ToString(CultureInfo.InvariantCulture),
            ["longitude"] = facility.Longitude?.ToString(CultureInfo.InvariantCulture),
            ["timeZone"] = facility.TimeZone,
            ["contactPhone"] = facility.ContactPhone,
            ["contactEmail"] = facility.ContactEmail,
            ["safetyMeasures"] = facility.SafetyMeasures,
            ["houseRules"] = facility.HouseRules
        };

    private static Dictionary<string, string?> AmenitySnapshot(Facility facility) =>
        new()
        {
            ["amenityIds"] = string.Join(
                ",",
                facility.Amenities.Select(link => link.AmenityId.ToString()).OrderBy(id => id))
        };

    private static Dictionary<string, string?> HoursSnapshot(Facility facility) =>
        facility.OperatingHours
            .OrderBy(hour => hour.DayOfWeek)
            .ToDictionary(
                hour => hour.DayOfWeek.ToString(),
                hour => hour.IsClosed ? "closed" : $"{hour.OpensAt:HH\\:mm}-{hour.ClosesAt:HH\\:mm}");

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
