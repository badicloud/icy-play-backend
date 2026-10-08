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

public sealed class FacilityOwnerEditService(
    AppDbContext db,
    IAuditLogger audit,
    IActivityCatalog catalog,
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
        catalog.Invalidate();
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

        // Posted by the browser, so never taken on trust.
        if (request.Photos.Any(photo => !assets.IsTrustedSecureUrl(photo.SecureUrl)))
        {
            return EditResult.Fail(EditFailure.UntrustedPhotoUrl);
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

        var photosBefore = await PhotoSnapshotAsync(facility.Id, ct);
        await PhotoGallery.ReplaceAsync(db, facility.Id, null, request.Photos, now, ct);

        audit.RecordChange(
            actor,
            AuditAction.FacilityPhotosUpdated,
            AuditEntityType.Facility,
            facility.Id,
            photosBefore,
            PhotoGallery.Snapshot(request.Photos),
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

    public async Task<EditResult> UpdateContractTermAsync(
        Guid facilityOwnerId,
        Guid contractId,
        UpdateContractTermRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .Include(candidate => candidate.Contracts)
            .SingleOrDefaultAsync(candidate => candidate.Id == facilityOwnerId, ct);

        var contract = owner?.Contracts.FirstOrDefault(candidate => candidate.Id == contractId);

        if (contract is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        if (contract.CancelledAt is not null)
        {
            return EditResult.Fail(EditFailure.AlreadyCancelled);
        }

        // The same rule renewal uses: two live terms covering one day cannot
        // both be the one fees are calculated against. This term is left out of
        // the comparison, or it would overlap itself.
        var overlaps = owner!.Contracts.Any(candidate =>
            candidate.Id != contractId &&
            candidate.CancelledAt is null &&
            candidate.StartDate <= request.EndDate &&
            request.StartDate <= candidate.EndDate);

        if (overlaps)
        {
            return EditResult.Fail(EditFailure.OverlappingContract);
        }

        var now = timeProvider.GetUtcNow();
        var before = TermSnapshot(contract);
        contract.Reschedule(request.StartDate, request.EndDate, request.Notes, now);

        audit.RecordChange(
            actor,
            AuditAction.ContractTermUpdated,
            AuditEntityType.FacilityOwnerContract,
            contract.Id,
            before,
            TermSnapshot(contract),
            request.Reason);

        await db.SaveChangesAsync(ct);
        catalog.Invalidate();
        logger.LogInformation(
            "Contract {ContractId} was rescheduled by {ActorUserId}.",
            contract.Id,
            actor.UserId);
        return EditResult.Success();
    }

    private static Dictionary<string, string?> TermSnapshot(FacilityOwnerContract contract) =>
        new()
        {
            ["startDate"] = contract.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = contract.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["notes"] = contract.Notes
        };

    public async Task<EditResult> UpdatePaymentDetailsAsync(
        Guid facilityOwnerId,
        UpdatePaymentDetailsRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = await db.FacilityOwners
            .SingleOrDefaultAsync(candidate => candidate.Id == facilityOwnerId, ct);

        if (owner is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        // The QR code is posted by the browser after it uploads, so the link
        // cannot be taken on trust: without this a venue's payment code could be
        // pointed at any image on the internet.
        if (!string.IsNullOrWhiteSpace(request.GcashQrCodeUrl) &&
            !assets.IsTrustedSecureUrl(request.GcashQrCodeUrl))
        {
            return EditResult.Fail(EditFailure.UntrustedAssetUrl);
        }

        var now = timeProvider.GetUtcNow();
        var before = PaymentSnapshot(owner);

        owner.SetPaymentDetails(
            request.GcashNumber,
            request.GcashAccountName,
            request.GcashQrCodeUrl,
            request.PartialBookingExpiryMinutes,
            now);

        audit.RecordChange(
            actor,
            AuditAction.FacilityOwnerPaymentDetailsUpdated,
            AuditEntityType.FacilityOwner,
            owner.Id,
            before,
            PaymentSnapshot(owner),
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Payment details for owner {FacilityOwnerId} were updated by {ActorUserId}.",
            owner.Id,
            actor.UserId);

        return EditResult.Success();
    }

    public async Task<EditResult> UpdateBookingRulesAsync(
        Guid facilityOwnerId,
        UpdateBookingRulesRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = await db.FacilityOwners
            .SingleOrDefaultAsync(candidate => candidate.Id == facilityOwnerId, ct);

        if (owner is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        var before = BookingRulesSnapshot(owner);

        owner.SetBookingWindow(request.BookingWindowDays, now);
        owner.SetMoveLimit(request.MoveLimit, now);
        owner.SetMoveNotice(request.MoveNoticeDays, now);

        audit.RecordChange(
            actor,
            AuditAction.FacilityOwnerBookingRulesUpdated,
            AuditEntityType.FacilityOwner,
            owner.Id,
            before,
            BookingRulesSnapshot(owner),
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Booking rules for owner {FacilityOwnerId} were updated by {ActorUserId}.",
            owner.Id,
            actor.UserId);

        return EditResult.Success();
    }

    private static Dictionary<string, string?> BookingRulesSnapshot(FacilityOwner owner) =>
        new()
        {
            ["bookingWindowDays"] = owner.BookingWindowDays.ToString(CultureInfo.InvariantCulture),
            ["moveLimit"] = owner.MoveLimit.ToString(CultureInfo.InvariantCulture),
            ["moveNoticeDays"] = owner.MoveNoticeDays.ToString(CultureInfo.InvariantCulture)
        };

    /// <summary>
    /// The QR code's link is not recorded: it is long, it changes whenever the
    /// picture is replaced, and the trail wants to say THAT the code changed
    /// rather than reprint it.
    /// </summary>
    private static Dictionary<string, string?> PaymentSnapshot(FacilityOwner owner) =>
        new()
        {
            ["gcashNumber"] = owner.GcashNumber,
            ["gcashAccountName"] = owner.GcashAccountName,
            ["gcashQrCode"] = owner.GcashQrCodeUrl is null ? "none" : "set",
            ["partialBookingExpiryMinutes"] =
                owner.PartialBookingExpiryMinutes.ToString(CultureInfo.InvariantCulture)
        };

    public async Task<EditResult> UpdateContractRatesAsync(
        Guid facilityOwnerId,
        Guid contractId,
        UpdateContractRatesRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        // Scoped to the owner in the same query, so another owner's term is
        // answered the same way as a missing one.
        var contract = await db.FacilityOwnerContracts
            .SingleOrDefaultAsync(
                candidate => candidate.Id == contractId && candidate.FacilityOwnerId == facilityOwnerId,
                ct);

        if (contract is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        var before = RateSnapshot(contract);
        contract.SetRates(request.PlatformHourlyRate, request.CommissionPercentage, now);

        if (request.PaymentMode is not null || request.OnlineHoldMinutes is not null)
        {
            contract.SetPaymentTerms(
                request.PaymentMode ?? contract.PaymentMode,
                request.OnlineHoldMinutes ?? contract.OnlineHoldMinutes,
                now);
        }

        audit.RecordChange(
            actor,
            AuditAction.ContractRatesUpdated,
            AuditEntityType.FacilityOwnerContract,
            contract.Id,
            before,
            RateSnapshot(contract),
            request.Reason);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Rates on contract {ContractId} were updated by {ActorUserId}.",
            contract.Id,
            actor.UserId);
        return EditResult.Success();
    }

    private static Dictionary<string, string?> RateSnapshot(FacilityOwnerContract contract) =>
        new()
        {
            ["platformHourlyRate"] =
                contract.PlatformHourlyRate.ToString("0.00", CultureInfo.InvariantCulture),
            ["commissionPercentage"] =
                contract.CommissionPercentage.ToString("0.00", CultureInfo.InvariantCulture),
            // Part of what was signed, so a switch between receipts and the
            // gateway is on the trail with who did it and why.
            ["paymentMode"] = contract.PaymentMode,
            ["onlineHoldMinutes"] = contract.OnlineHoldMinutes.ToString(CultureInfo.InvariantCulture)
        };

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
        catalog.Invalidate();
        logger.LogInformation("Contract {ContractId} was cancelled by {ActorUserId}.", contractId, actor.UserId);
        return EditResult.Success();
    }

    public async Task<EditResult> ActivateContractAsync(
        Guid facilityOwnerId,
        Guid contractId,
        ActivateContractRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .Include(candidate => candidate.Contracts)
            .SingleOrDefaultAsync(candidate => candidate.Id == facilityOwnerId, ct);

        var contract = owner?.Contracts.FirstOrDefault(candidate => candidate.Id == contractId);

        if (contract is null)
        {
            return EditResult.Fail(EditFailure.NotFound);
        }

        if (contract.CancelledAt is not null)
        {
            return EditResult.Fail(EditFailure.AlreadyCancelled);
        }

        // The same "today" the console uses to mark a term live, so the button
        // and the badge never disagree about whether a term has begun.
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        if (contract.StartDate <= today)
        {
            return EditResult.Fail(EditFailure.AlreadyStarted);
        }

        // Brought forward, it covers today too, and two live terms on one day
        // cannot both be the one fees are worked out against.
        var overlaps = owner!.Contracts.Any(candidate =>
            candidate.Id != contractId &&
            candidate.CancelledAt is null &&
            candidate.StartDate <= contract.EndDate &&
            today <= candidate.EndDate);

        if (overlaps)
        {
            return EditResult.Fail(EditFailure.OverlappingContract);
        }

        var before = TermSnapshot(contract);
        contract.Reschedule(today, contract.EndDate, contract.Notes, now);

        audit.RecordChange(
            actor,
            AuditAction.ContractActivated,
            AuditEntityType.FacilityOwnerContract,
            contract.Id,
            before,
            TermSnapshot(contract),
            request.Reason);

        await db.SaveChangesAsync(ct);
        // The owner's courts may have been off sale with no live term; they
        // are back on it now.
        catalog.Invalidate();
        logger.LogInformation(
            "Contract {ContractId} was brought forward to start today by {ActorUserId}.",
            contract.Id,
            actor.UserId);
        return EditResult.Success();
    }

    public async Task<PagedResult<ActivityEntry>> ListActivityAsync(
        Guid facilityOwnerId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ActivityPaging.LargestPage);

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

        // Open plays too, whether the desk or the platform made the change. A
        // deleted draft has no row left to say whose it was, so its entries are
        // not gathered here; it never reached a customer.
        var openPlayIds = await db.OpenPlays
            .Where(openPlay => facilityIds.Contains(openPlay.FacilityId))
            .Select(openPlay => openPlay.Id)
            .ToArrayAsync(ct);

        var trail = db.AuditLogs
            .AsNoTracking()
            .Where(entry =>
                (entry.EntityType == AuditEntityType.FacilityOwner && entry.EntityId == facilityOwnerId) ||
                (entry.EntityType == AuditEntityType.Facility && facilityIds.Contains(entry.EntityId)) ||
                (entry.EntityType == AuditEntityType.FacilityOwnerContract &&
                    contractIds.Contains(entry.EntityId)) ||
                (entry.EntityType == AuditEntityType.OpenPlay && openPlayIds.Contains(entry.EntityId)));

        var total = await trail.CountAsync(ct);

        var entries = await trail
            // The id breaks a tie between two entries written in the same
            // instant, so a row cannot turn up on two pages or on neither.
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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

        return new PagedResult<ActivityEntry>(
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
            ],
            page,
            pageSize,
            total);
    }

    /// <summary>
    /// Rows are added and removed through the set, with the navigation kept in
    /// step so the audit snapshot taken afterwards is truthful. Going through
    /// the navigation alone is not enough: these entities carry a
    /// client-generated key, and EF reads an already-set key as "this row
    /// exists", marking the entity Modified and issuing an UPDATE for a row
    /// that was never inserted.
    /// </summary>
    private async Task<Dictionary<string, string?>> PhotoSnapshotAsync(
        Guid facilityId,
        CancellationToken ct)
    {
        var photos = await db.Photos
            .AsNoTracking()
            .Where(photo => photo.FacilityId == facilityId && photo.CourtId == null)
            .Select(photo => new { photo.PublicId, photo.IsCover })
            .ToListAsync(ct);

        return new Dictionary<string, string?>
        {
            ["photos"] = photos.Count.ToString(CultureInfo.InvariantCulture),
            ["cover"] = photos.FirstOrDefault(photo => photo.IsCover)?.PublicId
        };
    }

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
