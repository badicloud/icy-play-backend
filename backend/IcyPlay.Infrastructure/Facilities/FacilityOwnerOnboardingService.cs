using System.Security.Cryptography;
using IcyPlay.Application.Common;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// The admin console's onboarding, which is the only way a facility owner comes
/// into existence. Customers pay owners directly, so an account that could
/// become bookable without anyone from the platform having looked at it turns a
/// scam into a reputation problem for IcyPlay.
/// </summary>
public sealed class FacilityOwnerOnboardingService(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    ICloudinaryAssetService assets,
    IAccountInvitationService invitations,
    TimeProvider timeProvider,
    ILogger<FacilityOwnerOnboardingService> logger) : IFacilityOwnerOnboardingService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    private static readonly Dictionary<string, Func<IQueryable<FacilityOwner>, bool, IQueryable<FacilityOwner>>> Sorts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["businessName"] = (query, descending) =>
                descending ? query.OrderByDescending(x => x.BusinessName) : query.OrderBy(x => x.BusinessName),
            ["createdAt"] = (query, descending) =>
                descending ? query.OrderByDescending(x => x.CreatedAt) : query.OrderBy(x => x.CreatedAt)
        };

    public async Task<OnboardingResult<OnboardedFacilityOwnerResponse>> OnboardAsync(
        OnboardFacilityOwnerRequest request,
        Guid onboardedByUserId,
        CancellationToken ct)
    {
        if (!IsKnownTimeZone(request.Facility.TimeZone))
        {
            return OnboardingResult<OnboardedFacilityOwnerResponse>.Fail(OnboardingFailure.UnknownTimeZone);
        }

        // The browser posts this metadata straight from Cloudinary, so it cannot
        // be taken on trust: an http URL, or one on another cloud, would
        // otherwise be rendered to other users.
        if (request.Documents.Any(document => !assets.IsTrustedSecureUrl(document.SecureUrl)))
        {
            return OnboardingResult<OnboardedFacilityOwnerResponse>.Fail(OnboardingFailure.UntrustedAssetUrl);
        }

        var amenityIds = request.Facility.AmenityIds.Distinct().ToArray();
        if (amenityIds.Length > 0)
        {
            var known = await db.Amenities
                .Where(amenity => amenityIds.Contains(amenity.Id) && amenity.IsActive)
                .CountAsync(ct);
            if (known != amenityIds.Length)
            {
                return OnboardingResult<OnboardedFacilityOwnerResponse>.Fail(OnboardingFailure.UnknownAmenity);
            }
        }

        var now = timeProvider.GetUtcNow();
        var email = request.Owner.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(user => user.Email == email, ct))
        {
            return OnboardingResult<OnboardedFacilityOwnerResponse>.Fail(OnboardingFailure.DuplicateEmail);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var user = new User(email, request.Owner.FullName, request.Owner.PhoneNumber);

        // Nobody, the encoding admin included, ever knows this password. The
        // owner sets their own through the emailed link.
        user.SetPasswordHash(passwordHasher.HashPassword(user, GenerateUnguessablePassword()));
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole(user.Id, UserRoleName.FacilityOwner));

        var owner = new FacilityOwner(
            user.Id,
            request.Business.BusinessName,
            request.Business.BillingEmail,
            request.Business.BillingPhone);
        owner.UpdateBusinessDetails(
            request.Business.BusinessName,
            request.Business.BillingEmail,
            request.Business.BillingPhone,
            request.Business.BusinessRegistrationNumber,
            now);
        db.FacilityOwners.Add(owner);

        foreach (var document in request.Documents)
        {
            db.FacilityOwnerDocuments.Add(new FacilityOwnerDocument(
                owner.Id,
                document.DocumentType,
                document.PublicId,
                document.SecureUrl,
                document.FileName,
                document.ContentType,
                document.SizeInBytes,
                now));
        }

        var facility = new Facility(
            owner.Id,
            request.Facility.Name,
            await ReserveSlugAsync(request.Facility.Name, ct),
            request.Facility.Description,
            new FacilityAddress(
                request.Facility.AddressLine1,
                request.Facility.AddressLine2,
                request.Facility.City,
                request.Facility.Province,
                request.Facility.PostalCode,
                request.Facility.Country),
            new FacilityContact(request.Facility.ContactPhone, request.Facility.ContactEmail),
            new FacilityPolicies(request.Facility.SafetyMeasures, request.Facility.HouseRules),
            request.Facility.TimeZone,
            now);
        facility.SetCoordinates(request.Facility.Latitude, request.Facility.Longitude, now);
        db.Facilities.Add(facility);

        foreach (var hour in request.OperatingHours)
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

        var contract = new FacilityOwnerContract(
            owner.Id,
            request.Contract.StartDate,
            request.Contract.EndDate,
            onboardedByUserId,
            request.Contract.Notes,
            now);
        db.FacilityOwnerContracts.Add(contract);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            // Two admins encoding the same business in the same moment. The
            // index is the only place that can settle it.
            logger.LogWarning(exception, "Onboarding hit a unique constraint and was rolled back.");
            await transaction.RollbackAsync(ct);
            return OnboardingResult<OnboardedFacilityOwnerResponse>.Fail(OnboardingFailure.DuplicateSlug);
        }

        var invitationSent = await SendInvitationAsync(user, owner.BusinessName, ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Facility owner {FacilityOwnerId} was onboarded by {AdminUserId}.",
            owner.Id,
            onboardedByUserId);

        // Ask the domain rather than assuming: a contract that starts next month
        // leaves the owner Pending, not Commenced. Change tracking has already
        // put the new contract on the navigation collection, so adding it again
        // here would double it for anyone reading through this same context.
        var status = owner.StatusOn(DateOnly.FromDateTime(now.UtcDateTime));

        return OnboardingResult<OnboardedFacilityOwnerResponse>.Success(new OnboardedFacilityOwnerResponse(
            user.Id,
            owner.Id,
            facility.Id,
            facility.Slug,
            status.ToString(),
            invitationSent));
    }

    public async Task<FacilityOwnerDetail?> GetAsync(Guid id, CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.UserId,
                candidate.BusinessName,
                candidate.BillingEmail,
                candidate.BillingPhone,
                candidate.BusinessRegistrationNumber,
                candidate.IsActive,
                candidate.CreatedAt,
                candidate.UpdatedAt,
                Owner = new
                {
                    candidate.User.FullName,
                    candidate.User.Email,
                    candidate.User.PhoneNumber,
                    candidate.User.IsActive,
                    candidate.User.EmailVerifiedAt,
                    candidate.User.CreatedAt
                },
                Documents = candidate.Documents
                    .OrderBy(document => document.DocumentType)
                    .Select(document => new OwnerDocumentDetail(
                        document.Id,
                        document.DocumentType,
                        document.PublicId,
                        document.SecureUrl,
                        document.FileName,
                        document.ContentType,
                        document.SizeInBytes,
                        document.CreatedAt))
                    .ToList(),
                Facilities = candidate.Facilities
                    .OrderBy(facility => facility.Name)
                    .Select(facility => new FacilityDetail(
                        facility.Id,
                        facility.Name,
                        facility.Slug,
                        facility.Description,
                        facility.AddressLine1,
                        facility.AddressLine2,
                        facility.City,
                        facility.Province,
                        facility.PostalCode,
                        facility.Country,
                        facility.Latitude,
                        facility.Longitude,
                        facility.TimeZone,
                        facility.ContactPhone,
                        facility.ContactEmail,
                        facility.SafetyMeasures,
                        facility.HouseRules,
                        facility.IsActive,
                        facility.Amenities
                            .OrderBy(link => link.Amenity.Category)
                            .ThenBy(link => link.Amenity.DisplayOrder)
                            .Select(link => new FacilityAmenityDetail(
                                link.Amenity.Id,
                                link.Amenity.Key,
                                link.Amenity.Name,
                                link.Amenity.Category))
                            .ToList(),
                        facility.OperatingHours
                            .OrderBy(hour => hour.DayOfWeek)
                            .Select(hour => new FacilityOperatingHourDetail(
                                (int)hour.DayOfWeek,
                                hour.OpensAt,
                                hour.ClosesAt))
                            .ToList(),
                        facility.CreatedAt,
                        facility.UpdatedAt))
                    .ToList(),
                Contracts = candidate.Contracts
                    .OrderByDescending(contract => contract.StartDate)
                    .Select(contract => new
                    {
                        contract.Id,
                        contract.StartDate,
                        contract.EndDate,
                        contract.Notes,
                        contract.CommencedByUserId,
                        contract.CancelledAt,
                        contract.CreatedAt
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);

        if (owner is null)
        {
            return null;
        }

        // Resolved in one extra query rather than a join per contract, because a
        // renewed owner has several terms and usually one admin behind them all.
        var adminIds = owner.Contracts.Select(contract => contract.CommencedByUserId).Distinct().ToArray();
        var adminNames = await db.Users
            .AsNoTracking()
            .Where(user => adminIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName })
            .ToDictionaryAsync(user => user.Id, user => user.FullName, ct);

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        var latestInvitation = await db.AccountInvitationTokens
            .AsNoTracking()
            .Where(token => token.UserId == owner.UserId)
            .OrderByDescending(token => token.CreatedAt)
            .Select(token => new { token.CreatedAt, token.ExpiresAt, token.AcceptedAt })
            .FirstOrDefaultAsync(ct);

        return new FacilityOwnerDetail(
            owner.Id,
            owner.UserId,
            owner.BusinessName,
            owner.BillingEmail,
            owner.BillingPhone,
            owner.BusinessRegistrationNumber,
            owner.IsActive,
            FacilityOwner.DeriveStatus(
                owner.IsActive,
                owner.Contracts
                    .Where(contract => contract.CancelledAt is null)
                    .Select(contract => new ContractTerm(contract.StartDate, contract.EndDate)),
                today).ToString(),
            owner.CreatedAt,
            owner.UpdatedAt,
            new OwnerAccountDetail(
                owner.Owner.FullName,
                owner.Owner.Email,
                owner.Owner.PhoneNumber,
                owner.Owner.IsActive,
                owner.Owner.EmailVerifiedAt is not null,
                owner.Owner.EmailVerifiedAt,
                owner.Owner.CreatedAt),
            owner.Documents,
            owner.Facilities,
            [.. owner.Contracts.Select(contract => new ContractDetail(
                contract.Id,
                contract.StartDate,
                contract.EndDate,
                contract.Notes,
                contract.CommencedByUserId,
                adminNames.GetValueOrDefault(contract.CommencedByUserId),
                contract.CancelledAt,
                contract.CancelledAt is null &&
                    contract.StartDate <= today &&
                    today <= contract.EndDate,
                contract.CreatedAt))],
            new InvitationStatus(
                latestInvitation?.AcceptedAt is not null,
                latestInvitation?.CreatedAt,
                latestInvitation?.ExpiresAt,
                latestInvitation is not null &&
                    latestInvitation.AcceptedAt is null &&
                    latestInvitation.ExpiresAt > now));
    }

    public async Task<bool> ResendInvitationAsync(Guid id, CancellationToken ct)
    {
        var owner = await db.FacilityOwners
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new
            {
                candidate.UserId,
                candidate.BusinessName,
                candidate.User.Email,
                candidate.User.FullName
            })
            .FirstOrDefaultAsync(ct);

        if (owner is null)
        {
            return false;
        }

        await invitations.SendAsync(owner.UserId, owner.Email, owner.FullName, owner.BusinessName, ct);
        return true;
    }

    public async Task<PagedResult<FacilityOwnerListItem>?> ListAsync(
        FacilityOwnerQuery query,
        CancellationToken ct)
    {
        var sortBy = string.IsNullOrWhiteSpace(query.SortBy) ? "createdAt" : query.SortBy.Trim();
        if (!Sorts.TryGetValue(sortBy, out var applySort))
        {
            return null;
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize,
            1,
            MaximumPageSize);
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase) ||
            (query.SortDirection is null && sortBy.Equals("createdAt", StringComparison.OrdinalIgnoreCase));

        var owners = db.FacilityOwners.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            owners = owners.Where(owner =>
                EF.Functions.Like(owner.BusinessName, $"%{search}%") ||
                EF.Functions.Like(owner.User.Email, $"%{search}%") ||
                EF.Functions.Like(owner.User.FullName, $"%{search}%"));
        }

        var totalItems = await owners.CountAsync(ct);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var rows = await applySort(owners, descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(owner => new
            {
                owner.Id,
                owner.UserId,
                owner.BusinessName,
                OwnerName = owner.User.FullName,
                owner.User.Email,
                owner.BillingPhone,
                owner.IsActive,
                owner.CreatedAt,
                FacilityCount = owner.Facilities.Count,
                // Only what the status needs, rather than every term ever signed.
                Contracts = owner.Contracts
                    .Where(contract => contract.CancelledAt == null)
                    .OrderByDescending(contract => contract.EndDate)
                    .Select(contract => new { contract.StartDate, contract.EndDate })
                    .ToList()
            })
            .ToListAsync(ct);

        var items = rows
            .Select(row =>
            {
                var live = row.Contracts.FirstOrDefault(
                    contract => contract.StartDate <= today && today <= contract.EndDate);
                var latest = live ?? row.Contracts.FirstOrDefault();

                // The same rule the entity uses, not a second copy of it.
                var status = FacilityOwner.DeriveStatus(
                    row.IsActive,
                    row.Contracts.Select(contract => new ContractTerm(contract.StartDate, contract.EndDate)),
                    today);

                return new FacilityOwnerListItem(
                    row.Id,
                    row.UserId,
                    row.BusinessName,
                    row.OwnerName,
                    row.Email,
                    row.BillingPhone,
                    status.ToString(),
                    row.FacilityCount,
                    latest?.StartDate,
                    latest?.EndDate,
                    row.CreatedAt);
            });

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var wanted = query.Status.Trim();
            items = items.Where(item => item.Status.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        }

        return new PagedResult<FacilityOwnerListItem>(items.ToArray(), page, pageSize, totalItems);
    }

    public async Task<IReadOnlyCollection<AmenityListItem>> ListAmenitiesAsync(CancellationToken ct) =>
        await db.Amenities
            .AsNoTracking()
            .Where(amenity => amenity.IsActive)
            .OrderBy(amenity => amenity.Category)
            .ThenBy(amenity => amenity.DisplayOrder)
            .Select(amenity => new AmenityListItem(
                amenity.Id,
                amenity.Key,
                amenity.Name,
                amenity.Category,
                amenity.DisplayOrder))
            .ToArrayAsync(ct);

    /// <summary>
    /// Finds a slug nobody is using. Suffixed rather than rejected, because two
    /// venues legitimately share a name across two cities.
    /// </summary>
    private async Task<string> ReserveSlugAsync(string name, CancellationToken ct)
    {
        var baseSlug = Facility.ToSlug(name);
        var taken = await db.Facilities
            .Where(facility => facility.Slug == baseSlug || facility.Slug.StartsWith(baseSlug + "-"))
            .Select(facility => facility.Slug)
            .ToListAsync(ct);

        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Best effort. A Mailjet outage must not undo an onboarding the admin has
    /// already finished; the owner can ask for a fresh link themselves.
    /// </summary>
    private async Task<bool> SendInvitationAsync(User user, string businessName, CancellationToken ct)
    {
        try
        {
            await invitations.SendAsync(user.Id, user.Email, user.FullName, businessName, ct);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The onboarding invitation could not be sent to user {UserId}.",
                user.Id);
            return false;
        }
    }

    private static string GenerateUnguessablePassword() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true;
}
