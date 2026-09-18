using System.Globalization;
using System.Security.Cryptography;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// Builds a venue that looks like a real one, for demonstrating against.
///
/// Everything goes through the ordinary court service. Writing the rows
/// directly would be quicker and would produce a venue whose bookable courts,
/// audit trail and pricing were never built the way a real venue's are — and
/// the first thing it would do is behave differently from the thing it is
/// standing in for.
/// </summary>
public sealed class SeedService(
    AppDbContext db,
    ICourtService courts,
    IActivityCatalog catalog,
    IAuditLogger audit,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider) : ISeedService
{
    /// <summary>How the existing venue is set up, copied so the two match.</summary>
    private const int SlotLengthMinutes = 60;
    private const int MinimumDurationMinutes = 60;
    private const int Courts = 5;

    private static readonly TimeOnly OpensAt = new(6, 0);
    private static readonly TimeOnly ClosesAt = new(22, 0);
    private static readonly TimeOnly PeakStartsAt = new(17, 0);
    private static readonly TimeOnly PeakEndsAt = new(20, 0);

    /// <summary>
    /// What each court is marked out for, how many ways, and what each way
    /// costs. Taken from the venue this stands in for: the whole floor as
    /// basketball or volleyball, two badminton courts across it, three
    /// pickleball. No events — those are priced differently and are a separate
    /// conversation.
    /// </summary>
    private static readonly (string Key, int Divisions, decimal Standard, decimal Peak, decimal Weekend)[] Marked =
    [
        ("basketball", 1, 500m, 600m, 550m),
        ("badminton", 2, 400m, 600m, 550m),
        ("pickleball", 3, 300m, 350m, 550m),
        ("volleyball", 1, 500m, 600m, 550m)
    ];

    public async Task<SeedResult> BuildVenueAsync(
        AuditActor actor,
        string? ownerEmail,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // A term has to record who commenced it, and a seeded venue is no
        // exception: it is a real venue in every other respect.
        var commencedBy = actor.UserId
            ?? throw new InvalidOperationException("A venue can only be seeded by a signed-in admin.");

        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var tag = Guid.NewGuid().ToString("N")[..6];

        var sports = await db.Sports
            .AsNoTracking()
            .Where(sport => Marked.Select(marked => marked.Key).Contains(sport.Key))
            .ToDictionaryAsync(sport => sport.Key, sport => sport.Id, ct);

        var missing = Marked.Select(marked => marked.Key).Where(key => !sports.ContainsKey(key)).ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"The sports catalogue is missing {string.Join(", ", missing)}, so this venue cannot be built.");
        }

        // The only way into a seeded account is a password reset, so the
        // address has to be one somebody can actually read. They say which; a
        // blank one falls back to the throwaway inbox below.
        var email = string.IsNullOrWhiteSpace(ownerEmail)
            ? await DemoAddressAsync(tag, ct)
            : ownerEmail.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(candidate => candidate.Email == email, ct))
        {
            throw new InvalidOperationException($"{email} is already an account.");
        }

        var user = new User(email, $"Demo Venue {tag}", null);

        // A real hash of a password nobody will ever hold. Writing some
        // made-up string here instead would not be "no password" — it would be
        // a column the hasher cannot read, and resetting the password reads it
        // to check the new one differs from the old.
        user.SetPasswordHash(passwordHasher.HashPassword(user, UnguessablePassword()));
        user.MarkEmailVerified(now);
        db.Users.Add(user);

        // Without this the account signs in and can reach nothing: the desk
        // asks for the role, not for a row in FacilityOwners. Onboarding adds
        // it for the same reason, and a seeded owner is an owner.
        db.UserRoles.Add(new UserRole(user.Id, UserRoleName.FacilityOwner));

        var owner = new FacilityOwner(user.Id, $"Demo Sports Ventures {tag}", email, null);

        // Marked here, because here is the only place that knows. Afterwards a
        // seeded venue is indistinguishable from a real one except by the name
        // it happens to carry, and removing data by name is how a real venue
        // gets deleted.
        owner.MarkSeeded(now);
        db.FacilityOwners.Add(owner);

        // Without a term covering today the venue is built and invisible: the
        // public listing only offers owners who are live.
        db.FacilityOwnerContracts.Add(new FacilityOwnerContract(
            owner.Id,
            today.AddMonths(-1),
            today.AddMonths(11),
            commencedBy,
            "Seeded for demonstration.",
            now));

        await db.SaveChangesAsync(ct);

        var facilityName = $"Demo Sports Center {tag}";
        var built = new List<string>();
        Guid? facilityId = null;

        for (var number = 1; number <= Courts; number++)
        {
            var courtName = $"Court {number}";

            var created = await courts.CreateAsync(
                new CreateCourtRequest(
                    owner.Id,
                    facilityId,
                    facilityId is null ? NewFacility(facilityName) : null,
                    Court(courtName, sports)),
                actor,
                ct);

            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{courtName} could not be built: {created.Failure}.");
            }

            facilityId ??= created.Value!.FacilityId;

            var priced = await courts.UpdatePricingAsync(
                created.Value!.CourtId,
                Pricing(sports),
                actor,
                ct);

            if (!priced.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{courtName} could not be priced: {priced.Failure}.");
            }

            built.Add(courtName);
        }

        return new SeedResult(owner.Id, facilityId!.Value, facilityName, email, built);
    }

    public async Task<IReadOnlyCollection<SeededVenueSummary>> SeededVenuesAsync(CancellationToken ct)
    {
        var courtsOf = db.Courts;
        var bookableOf = db.BookableCourts;

        var venues = await db.FacilityOwners
            .AsNoTracking()
            .Where(owner => owner.SeededAt != null)
            .OrderBy(owner => owner.SeededAt)
            .Select(owner => new
            {
                owner.Id,
                owner.BusinessName,
                owner.User.Email,
                owner.SeededAt,
                Facilities = db.Facilities.Count(facility => facility.FacilityOwnerId == owner.Id),
                Courts = courtsOf.Count(court => court.FacilityOwnerId == owner.Id),
                // Counted through the court, because a booking records the
                // bookable court it was made on and nothing above that.
                Bookings = db.Bookings.Count(booking => bookableOf
                    .Where(bookable => courtsOf
                        .Where(court => court.FacilityOwnerId == owner.Id)
                        .Select(court => court.Id)
                        .Contains(bookable.CourtId))
                    .Select(bookable => bookable.Id)
                    .Contains(booking.BookableCourtId))
            })
            .ToListAsync(ct);

        return [.. venues.Select(venue => new SeededVenueSummary(
            venue.Id,
            venue.BusinessName,
            venue.Email,
            venue.SeededAt!.Value,
            venue.Facilities,
            venue.Courts,
            venue.Bookings))];
    }

    public async Task<SeedRemovalResult> RemoveSeededAsync(AuditActor actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var venues = await db.FacilityOwners
            .AsNoTracking()
            .Where(owner => owner.SeededAt != null)
            .Select(owner => new { owner.Id, owner.UserId })
            .ToListAsync(ct);

        if (venues.Count == 0)
        {
            return new SeedRemovalResult(0, 0, 0, 0);
        }

        var ownerIds = venues.Select(venue => venue.Id).ToArray();
        var userIds = venues.Select(venue => venue.UserId).ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var facilityIds = await db.Facilities
            .Where(facility => ownerIds.Contains(facility.FacilityOwnerId))
            .Select(facility => facility.Id)
            .ToArrayAsync(ct);

        // Both ways round, and deliberately so. A court carries its owner as
        // well as its facility, and the two are written at different times by
        // different code paths. Reading only the owner leaves behind any court
        // sitting in a seeded facility whose owner column says otherwise — and
        // "left behind" is not what happens to it, because the facility above
        // it is about to go and take it down by cascade, at which point its
        // bookings stop the whole removal halfway through.
        var courtIds = await db.Courts
            .Where(court => ownerIds.Contains(court.FacilityOwnerId)
                || facilityIds.Contains(court.FacilityId))
            .Select(court => court.Id)
            .ToArrayAsync(ct);

        var bookableIds = await db.BookableCourts
            .Where(bookable => courtIds.Contains(bookable.CourtId))
            .Select(bookable => bookable.Id)
            .ToArrayAsync(ct);

        var bookingIds = await db.Bookings
            .Where(booking => bookableIds.Contains(booking.BookableCourtId))
            .Select(booking => booking.Id)
            .ToArrayAsync(ct);

        // Deliberately by hand and child first, rather than leaning on the
        // cascades. A booking holds its bookable court under Restrict — money
        // against a court is why that court is retired rather than removed — so
        // a cascade from the top would stop halfway and leave a venue in
        // pieces. Going the other way round, every row pointing at a thing has
        // already gone by the time that thing does.
        await db.BookingMoveRequests
            .Where(request => bookingIds.Contains(request.BookingId))
            .ExecuteDeleteAsync(ct);

        await db.BookingSlots
            .Where(slot => bookingIds.Contains(slot.BookingId))
            .ExecuteDeleteAsync(ct);

        var removedBookings = await db.Bookings
            .Where(booking => bookingIds.Contains(booking.Id))
            .ExecuteDeleteAsync(ct);

        await db.BookableCourts
            .Where(bookable => courtIds.Contains(bookable.CourtId))
            .ExecuteDeleteAsync(ct);

        // Scoped by facility rather than by court: a court photo carries its
        // facility as well, so one condition reaches both the gallery of the
        // building and the gallery of every floor in it.
        await db.Photos
            .Where(photo => facilityIds.Contains(photo.FacilityId))
            .ExecuteDeleteAsync(ct);

        await db.MaintenancePeriods
            .Where(period => facilityIds.Contains(period.FacilityId))
            .ExecuteDeleteAsync(ct);

        await db.CourtOperatingHours
            .Where(hour => courtIds.Contains(hour.CourtId))
            .ExecuteDeleteAsync(ct);

        await db.CourtSports
            .Where(sport => courtIds.Contains(sport.CourtId))
            .ExecuteDeleteAsync(ct);

        var removedCourts = await db.Courts
            .Where(court => courtIds.Contains(court.Id))
            .ExecuteDeleteAsync(ct);

        await db.FacilityAttendants
            .Where(attendant => facilityIds.Contains(attendant.FacilityId))
            .ExecuteDeleteAsync(ct);

        await db.FacilityAmenities
            .Where(amenity => facilityIds.Contains(amenity.FacilityId))
            .ExecuteDeleteAsync(ct);

        await db.FacilityOperatingHours
            .Where(hour => facilityIds.Contains(hour.FacilityId))
            .ExecuteDeleteAsync(ct);

        var removedFacilities = await db.Facilities
            .Where(facility => facilityIds.Contains(facility.Id))
            .ExecuteDeleteAsync(ct);

        await db.FacilityOwnerDocuments
            .Where(document => ownerIds.Contains(document.FacilityOwnerId))
            .ExecuteDeleteAsync(ct);

        await db.FacilityOwnerContracts
            .Where(contract => ownerIds.Contains(contract.FacilityOwnerId))
            .ExecuteDeleteAsync(ct);

        var removedVenues = await db.FacilityOwners
            .Where(owner => ownerIds.Contains(owner.Id))
            .ExecuteDeleteAsync(ct);

        // The account as well, named table by table rather than left to the
        // cascade from Users. The seeder made this account and nothing else
        // uses it; leaving it behind means the same address can never be
        // seeded again.
        await db.UserRoles.Where(role => userIds.Contains(role.UserId)).ExecuteDeleteAsync(ct);
        await db.RefreshTokens.Where(token => userIds.Contains(token.UserId)).ExecuteDeleteAsync(ct);
        await db.EmailVerificationTokens.Where(token => userIds.Contains(token.UserId)).ExecuteDeleteAsync(ct);
        await db.PasswordResetTokens.Where(token => userIds.Contains(token.UserId)).ExecuteDeleteAsync(ct);
        await db.AccountInvitationTokens.Where(token => userIds.Contains(token.UserId)).ExecuteDeleteAsync(ct);
        await db.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync(ct);

        // The trail has no foreign key to any of this on purpose, so it is
        // still standing afterwards — and it is now the only record that these
        // venues were ever here.
        audit.RecordEvent(
            actor,
            AuditAction.SeededDataRemoved,
            AuditEntityType.FacilityOwner,
            ownerIds[0],
            new Dictionary<string, string?>
            {
                ["venues"] = removedVenues.ToString(CultureInfo.InvariantCulture),
                ["facilities"] = removedFacilities.ToString(CultureInfo.InvariantCulture),
                ["courts"] = removedCourts.ToString(CultureInfo.InvariantCulture),
                ["bookings"] = removedBookings.ToString(CultureInfo.InvariantCulture),
                ["facilityOwnerIds"] = string.Join(", ", ownerIds)
            },
            "Demonstration data removed.");

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // The public listing is cached for minutes at a time. Without this the
        // courts stay on offer after they have stopped existing, and the first
        // person to tap one gets an error rather than a booking.
        catalog.Invalidate();

        return new SeedRemovalResult(removedVenues, removedFacilities, removedCourts, removedBookings);
    }

    /// <summary>
    /// Long, random, and thrown away. The way into a seeded account is a
    /// password reset, not this.
    /// </summary>
    private static string UnguessablePassword() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Where a seeded venue's post goes when nobody says otherwise.
    ///
    /// The same address every time, on purpose: one inbox to open rather than
    /// a new address to invent and remember on each run. It is a throwaway
    /// public inbox — anyone who knows the name can read it, password reset
    /// links and all — which is fine for a venue that exists to be
    /// demonstrated against and is fine for nothing else.
    /// </summary>
    private const string DemoInbox = "demo-owner@mailinator.com";

    /// <summary>
    /// <see cref="DemoInbox"/>, or a neighbouring one when that address is
    /// already an account. Seeding a second venue without removing the first
    /// is an ordinary thing to do, and refusing it over the address would stop
    /// a run that has nothing else wrong with it.
    /// </summary>
    private async Task<string> DemoAddressAsync(string tag, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(candidate => candidate.Email == DemoInbox, ct))
        {
            return DemoInbox;
        }

        var at = DemoInbox.IndexOf('@');

        return $"{DemoInbox[..at]}-{tag}{DemoInbox[at..]}";
    }

    private static CourtInput Court(string name, IReadOnlyDictionary<string, Guid> sports) => new(
        name,
        10,
        "Seeded for demonstration.",
        [.. Marked.Select(marked => new CourtSportInput(sports[marked.Key], marked.Divisions))],
        sports["basketball"],
        CourtVenueType.Covered,
        CourtSurface.Concrete,
        true,
        "Full court",
        null,
        null,
        SlotLengthMinutes,
        MinimumDurationMinutes,
        0,
        true,
        [],
        []);

    private static UpdateCourtPricingRequest Pricing(IReadOnlyDictionary<string, Guid> sports) => new(
        [
            .. Marked.Select(marked => new SportPricingInput(
                sports[marked.Key],
                marked.Standard,
                marked.Peak,
                marked.Weekend,
                null))
        ],
        new PeakWindowInput(PeakStartsAt, PeakEndsAt, true, true),
        "Seeded for demonstration.");

    /// <summary>
    /// Open six in the morning to ten at night, and shut on Sundays — the same
    /// week the venue this copies keeps. A day with no hours is how a closure
    /// is said here.
    /// </summary>
    private static NewFacilityInput NewFacility(string name) => new(
        new FacilityInput(
            name,
            "Five courts, seeded for demonstration.",
            "123 Demo Street",
            null,
            "Cebu City",
            "Cebu",
            "6000",
            "Philippines",
            null,
            null,
            "Asia/Manila",
            "+639171234567",
            "hello@example.com",
            "First aid kit on site.",
            "No street shoes on the court.",
            [],
            []),
        [
            .. Enum.GetValues<DayOfWeek>()
                .Where(day => day != DayOfWeek.Sunday)
                .Select(day => new OperatingHourInput(day, OpensAt, ClosesAt))
        ],
        []);
}
