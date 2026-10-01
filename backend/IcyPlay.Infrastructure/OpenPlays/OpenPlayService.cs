using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Open plays for the desk and for the platform admin. The desk works the
/// venues its owner or attendant is on; the admin, one facility owner's venues.
/// Every action runs through one core per action with the venues as its scope,
/// so the two cannot drift apart on a rule.
///
/// A draft blocks nothing and can be changed freely; a clash is a warning on
/// save. Publishing is where a clash is refused, because that is the moment the
/// court is held. A published open play cannot be changed: players are
/// registering for what it says.
/// </summary>
public sealed class OpenPlayService(
    AppDbContext db,
    IAuditLogger audit,
    ICloudinaryAssetService assets,
    TimeProvider timeProvider) : IDeskOpenPlayService, IAdminOpenPlayService
{
    /// <summary>How many clashes come back. Enough to act on; the rest are the same problem.</summary>
    private const int ClashesShown = 50;

    /// <summary>How far ahead two open-ended series are compared for a first shared date.</summary>
    private const int ClashHorizonDays = 400;

    // ------------------------------------------------------------ the desk
    //
    // The venues the signed-in owner or attendant works.

    public Task<OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>> ListAsync(
        Guid userId, Guid? facilityId, CancellationToken ct) =>
        ListCore(DeskVenues(userId), facilityId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> GetAsync(Guid userId, Guid openPlayId, CancellationToken ct) =>
        GetCore(DeskVenues(userId), openPlayId, ct);

    public Task<OpenPlayResult<OpenPlaySaved>> CreateAsync(
        AuditActor actor, OpenPlayInput input, CancellationToken ct) =>
        CreateCore(DeskVenuesOf(actor), actor, input, ct);

    public Task<OpenPlayResult<OpenPlaySaved>> UpdateAsync(
        AuditActor actor, Guid openPlayId, OpenPlayInput input, CancellationToken ct) =>
        UpdateCore(DeskVenuesOf(actor), actor, openPlayId, input, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> PublishAsync(AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        PublishCore(DeskVenuesOf(actor), actor, openPlayId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> UnpublishAsync(AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        UnpublishCore(DeskVenuesOf(actor), actor, openPlayId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> EndAsync(AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        EndCore(DeskVenuesOf(actor), actor, openPlayId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> SetCoverPhotoAsync(
        AuditActor actor, Guid openPlayId, OpenPlayPhotoInput photo, CancellationToken ct) =>
        SetCoverPhotoCore(DeskVenuesOf(actor), actor, openPlayId, photo, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> RemoveCoverPhotoAsync(
        AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        RemoveCoverPhotoCore(DeskVenuesOf(actor), actor, openPlayId, ct);

    public Task<OpenPlayResult<bool>> DeleteDraftAsync(AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        DeleteDraftCore(DeskVenuesOf(actor), actor, openPlayId, ct);

    // ------------------------------------------------------------ the admin
    //
    // One facility owner's venues, whoever the admin is. The same cores and so
    // the same rules: an admin cannot publish over a booking or edit a
    // published open play any more than the desk can.

    public Task<OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>> ListForOwnerAsync(
        Guid facilityOwnerId, CancellationToken ct) =>
        ListCore(OwnerVenues(facilityOwnerId), null, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> GetForOwnerAsync(
        Guid facilityOwnerId, Guid openPlayId, CancellationToken ct) =>
        GetCore(OwnerVenues(facilityOwnerId), openPlayId, ct);

    public async Task<OpenPlayPlaces> PlacesForOwnerAsync(Guid facilityOwnerId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        var venues = await db.Facilities
            .AsNoTracking()
            .Where(facility => facility.FacilityOwnerId == facilityOwnerId)
            .OrderBy(facility => facility.Name)
            .Select(facility => new { facility.Id, facility.Name, facility.TimeZone })
            .ToListAsync(ct);

        var venueIds = venues.Select(venue => venue.Id).ToArray();

        var courts = await db.Courts
            .AsNoTracking()
            .Where(court => court.IsActive && venueIds.Contains(court.FacilityId))
            .OrderBy(court => court.Facility.Name)
            .ThenBy(court => court.DisplayOrder)
            .ThenBy(court => court.Name)
            .Select(court => new
            {
                court.Id,
                court.FacilityId,
                FacilityName = court.Facility.Name,
                court.Name,
                Units = court.Sports
                    .SelectMany(pair => pair.BookableCourts
                        .Where(unit => unit.IsActive)
                        .Select(unit => new
                        {
                            BookableCourtId = unit.Id,
                            SportName = pair.Sport.Name,
                            SportKey = pair.Sport.Key,
                            unit.DivisionNumber,
                            pair.Divisions
                        }))
                    .ToList()
            })
            .ToListAsync(ct);

        return new OpenPlayPlaces(
            [
                .. venues.Select(venue => new DeskVenue(
                    venue.Id,
                    venue.Name,
                    true,
                    DateOnly.FromDateTime(VenueClock.LocalNowIn(venue.TimeZone, now).DateTime)))
            ],
            [
                .. courts.Select(court => new DeskCourt(
                    court.Id,
                    court.FacilityId,
                    court.FacilityName,
                    court.Name,
                    [
                        .. court.Units
                            .OrderBy(unit => unit.SportName)
                            .ThenBy(unit => unit.DivisionNumber)
                            .Select(unit => new DeskCourtUnit(
                                unit.BookableCourtId,
                                unit.SportName,
                                unit.SportKey,
                                unit.DivisionNumber,
                                DeskService.UnitLabel(unit.SportName, unit.DivisionNumber, unit.Divisions)))
                    ]))
            ]);
    }

    public Task<OpenPlayResult<OpenPlaySaved>> CreateForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, OpenPlayInput input, CancellationToken ct) =>
        CreateCore(OwnerVenues(facilityOwnerId), actor, input, ct);

    public Task<OpenPlayResult<OpenPlaySaved>> UpdateForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, OpenPlayInput input, CancellationToken ct) =>
        UpdateCore(OwnerVenues(facilityOwnerId), actor, openPlayId, input, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> PublishForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        PublishCore(OwnerVenues(facilityOwnerId), actor, openPlayId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> UnpublishForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        UnpublishCore(OwnerVenues(facilityOwnerId), actor, openPlayId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> EndForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        EndCore(OwnerVenues(facilityOwnerId), actor, openPlayId, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> SetCoverPhotoForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, OpenPlayPhotoInput photo, CancellationToken ct) =>
        SetCoverPhotoCore(OwnerVenues(facilityOwnerId), actor, openPlayId, photo, ct);

    public Task<OpenPlayResult<DeskOpenPlay>> RemoveCoverPhotoForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        RemoveCoverPhotoCore(OwnerVenues(facilityOwnerId), actor, openPlayId, ct);

    public Task<OpenPlayResult<bool>> DeleteDraftForOwnerAsync(
        Guid facilityOwnerId, AuditActor actor, Guid openPlayId, CancellationToken ct) =>
        DeleteDraftCore(OwnerVenues(facilityOwnerId), actor, openPlayId, ct);

    /// <summary>
    /// The desk's scope for this actor. An actor with no user id works no venue,
    /// so the scope is empty and everything answers as not found.
    /// </summary>
    private IQueryable<Guid> DeskVenuesOf(AuditActor actor) =>
        DeskVenues(actor?.UserId ?? Guid.Empty);

    private async Task<OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>> ListCore(
        IQueryable<Guid> venues,
        Guid? facilityId,
        CancellationToken ct)
    {
        var venueIds = await venues.ToListAsync(ct);

        if (facilityId is Guid wanted)
        {
            if (!venueIds.Contains(wanted))
            {
                return OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>.Fail(OpenPlayFailure.NotAttended);
            }

            venueIds = [wanted];
        }

        var ids = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => venueIds.Contains(openPlay.FacilityId))
            .Select(openPlay => openPlay.Id)
            .ToArrayAsync(ct);

        return OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>.Success(await ProjectAsync(ids, ct));
    }

    private async Task<OpenPlayResult<DeskOpenPlay>> GetCore(IQueryable<Guid> venues, Guid openPlayId, CancellationToken ct)
    {
        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        return openPlay is null
            ? OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound)
            : OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
    }

    private async Task<OpenPlayResult<OpenPlaySaved>> CreateCore(
        IQueryable<Guid> venues,
        AuditActor actor,
        OpenPlayInput input,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.NotAttended);
        }

        var court = await CourtInScopeAsync(venues, input.BookableCourtId, ct);

        if (court is null)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.NotAttended);
        }

        var now = timeProvider.GetUtcNow();

        if (Check(court, input, now) is { } refused)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(refused.Failure, refused.Message);
        }

        OpenPlay openPlay;

        try
        {
            openPlay = new OpenPlay(
                court.Court.FacilityId,
                court.Id,
                court.CourtId,
                input.Title,
                input.Level,
                input.MaxPlayers,
                input.RegistrationFee,
                input.StartsAt,
                input.EndsAt,
                Days(input.Days),
                input.StartDate,
                input.EndDate,
                input.RegistrationCutoffMinutes,
                EarlyBird(input.EarlyBird),
                userId,
                now);
        }
        catch (ArgumentException problem)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.Invalid, Plain(problem));
        }

        db.OpenPlays.Add(openPlay);

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayCreated,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            Describe(openPlay),
            "Open play saved as a draft.");

        await db.SaveChangesAsync(ct);

        var clashes = await ClashesAsync(openPlay, court, ct);

        return OpenPlayResult<OpenPlaySaved>.Success(new OpenPlaySaved(await ProjectOneAsync(openPlay.Id, ct), clashes));
    }

    private async Task<OpenPlayResult<OpenPlaySaved>> UpdateCore(
        IQueryable<Guid> venues,
        AuditActor actor,
        Guid openPlayId,
        OpenPlayInput input,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.NotFound);
        }

        if (openPlay.HasEnded)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.Ended);
        }

        if (openPlay.IsPublished)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.NotADraft);
        }

        // Moving a draft to another court is allowed, but only to one at the
        // same venue: a draft does not change hands between venues.
        var court = await CourtInScopeAsync(venues, input.BookableCourtId, ct);

        if (court is null || court.Court.FacilityId != openPlay.FacilityId)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(
                OpenPlayFailure.Invalid,
                "Pick a court at the same venue as the open play.");
        }

        var now = timeProvider.GetUtcNow();

        if (Check(court, input, now) is { } refused)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(refused.Failure, refused.Message);
        }

        var before = Describe(openPlay);

        try
        {
            openPlay.Update(
                input.Title,
                input.Level,
                input.MaxPlayers,
                input.RegistrationFee,
                input.StartsAt,
                input.EndsAt,
                Days(input.Days),
                input.StartDate,
                input.EndDate,
                input.RegistrationCutoffMinutes,
                EarlyBird(input.EarlyBird),
                now);
        }
        catch (ArgumentException problem)
        {
            return OpenPlayResult<OpenPlaySaved>.Fail(OpenPlayFailure.Invalid, Plain(problem));
        }

        openPlay.MoveTo(court.Id, court.CourtId, now);

        audit.RecordChange(
            actor,
            AuditAction.OpenPlayUpdated,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            before,
            Describe(openPlay));

        await db.SaveChangesAsync(ct);

        var clashes = await ClashesAsync(openPlay, court, ct);

        return OpenPlayResult<OpenPlaySaved>.Success(new OpenPlaySaved(await ProjectOneAsync(openPlay.Id, ct), clashes));
    }

    private async Task<OpenPlayResult<DeskOpenPlay>> PublishCore(IQueryable<Guid> venues, AuditActor actor, Guid openPlayId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        if (openPlay.HasEnded)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.Ended);
        }

        if (openPlay.IsPublished)
        {
            return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
        }

        var court = await CourtInScopeAsync(venues, openPlay.BookableCourtId, ct);

        if (court is null)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotAttended);
        }

        // Asked again at publishing: the court's hours can have changed since
        // the draft was saved.
        if (OutsideHours(court, openPlay.Days, openPlay.StartsAt, openPlay.EndsAt) is string closed)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.OutsideOpeningHours, closed);
        }

        var clashes = await ClashesAsync(openPlay, court, ct);

        if (clashes.Count > 0)
        {
            return OpenPlayResult<DeskOpenPlay>.Clashing(clashes);
        }

        var now = timeProvider.GetUtcNow();
        openPlay.Publish(userId, now);

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayPublished,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            Describe(openPlay),
            "Open play published: open for registration, and the court is held for it.");

        await db.SaveChangesAsync(ct);

        return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
    }

    private async Task<OpenPlayResult<DeskOpenPlay>> UnpublishCore(IQueryable<Guid> venues, AuditActor actor, Guid openPlayId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        if (openPlay.HasEnded)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.Ended);
        }

        if (!openPlay.IsPublished)
        {
            return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
        }

        if (await LiveRegistrationsAsync(openPlay.Id, null, ct) > 0)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.HasRegistrations);
        }

        openPlay.Unpublish(timeProvider.GetUtcNow());

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayUnpublished,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            Describe(openPlay),
            "Open play taken back to a draft: hidden, and the court released.");

        await db.SaveChangesAsync(ct);

        return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
    }

    private async Task<OpenPlayResult<DeskOpenPlay>> EndCore(IQueryable<Guid> venues, AuditActor actor, Guid openPlayId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        if (openPlay.HasEnded)
        {
            return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
        }

        if (!openPlay.IsPublished)
        {
            // A draft has nothing to end. Deleting it is the way to be rid of it.
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotPublished);
        }

        var now = timeProvider.GetUtcNow();
        var today = await VenueTodayAsync(openPlay.FacilityId, now, ct);

        // Today's session still happens: players may already be on their way.
        // Every date after it is released.
        var later = await db.OpenPlaySessions
            .Include(session => session.Registrations)
            .Where(session => session.OpenPlayId == openPlay.Id && session.Date > today)
            .ToListAsync(ct);

        foreach (var session in later)
        {
            session.Cancel("The venue ended this open play.", userId, now);
        }

        openPlay.End(today, now);

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayEnded,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            new Dictionary<string, string?>
            {
                ["lastDate"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["sessionsCancelled"] = later.Count.ToString(CultureInfo.InvariantCulture)
            },
            "Open play ended. Every later date is released.");

        await db.SaveChangesAsync(ct);

        return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
    }

    private async Task<OpenPlayResult<DeskOpenPlay>> SetCoverPhotoCore(
        IQueryable<Guid> venues,
        AuditActor actor,
        Guid openPlayId,
        OpenPlayPhotoInput photo,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        if (openPlay.HasEnded)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.Ended);
        }

        // Posted by the browser, so not trusted: the URL has to be on the
        // platform's own Cloudinary account, and the file in the folder the
        // desk's signature allows. Anything else could point a public page at
        // a host of somebody's choosing.
        if (photo is null
            || string.IsNullOrWhiteSpace(photo.PublicId)
            || !photo.PublicId.StartsWith(OpenPlayPhotos.Folder + "/", StringComparison.Ordinal)
            || !assets.IsTrustedSecureUrl(photo.SecureUrl))
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.UntrustedPhoto);
        }

        var before = openPlay.CoverPhotoPublicId;
        openPlay.SetCoverPhoto(photo.PublicId, photo.SecureUrl, timeProvider.GetUtcNow());

        audit.RecordChange(
            actor,
            AuditAction.OpenPlayPhotoChanged,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            new Dictionary<string, string?> { ["coverPhoto"] = before },
            new Dictionary<string, string?> { ["coverPhoto"] = openPlay.CoverPhotoPublicId });

        await db.SaveChangesAsync(ct);

        return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
    }

    private async Task<OpenPlayResult<DeskOpenPlay>> RemoveCoverPhotoCore(
        IQueryable<Guid> venues,
        AuditActor actor,
        Guid openPlayId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.NotFound);
        }

        if (openPlay.HasEnded)
        {
            return OpenPlayResult<DeskOpenPlay>.Fail(OpenPlayFailure.Ended);
        }

        if (openPlay.CoverPhotoPublicId is { } before)
        {
            openPlay.RemoveCoverPhoto(timeProvider.GetUtcNow());

            audit.RecordChange(
                actor,
                AuditAction.OpenPlayPhotoChanged,
                AuditEntityType.OpenPlay,
                openPlay.Id,
                new Dictionary<string, string?> { ["coverPhoto"] = before },
                new Dictionary<string, string?> { ["coverPhoto"] = null });

            await db.SaveChangesAsync(ct);
        }

        return OpenPlayResult<DeskOpenPlay>.Success(await ProjectOneAsync(openPlay.Id, ct));
    }

    private async Task<OpenPlayResult<bool>> DeleteDraftCore(IQueryable<Guid> venues, AuditActor actor, Guid openPlayId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.UserId is not Guid userId)
        {
            return OpenPlayResult<bool>.Fail(OpenPlayFailure.NotFound);
        }

        var openPlay = await ForScopeAsync(venues, openPlayId, ct);

        if (openPlay is null)
        {
            return OpenPlayResult<bool>.Fail(OpenPlayFailure.NotFound);
        }

        // Only a draft that has never been published. One that was published
        // and taken back may have sessions on record, and those stay.
        if (openPlay.IsPublished || openPlay.HasEnded
            || await db.OpenPlaySessions.AnyAsync(session => session.OpenPlayId == openPlay.Id, ct))
        {
            return OpenPlayResult<bool>.Fail(OpenPlayFailure.NotADraft);
        }

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayDraftDeleted,
            AuditEntityType.OpenPlay,
            openPlay.Id,
            Describe(openPlay),
            "Draft open play deleted.");

        db.OpenPlays.Remove(openPlay);
        await db.SaveChangesAsync(ct);

        return OpenPlayResult<bool>.Success(true);
    }

    // ------------------------------------------------------------ the checks

    private sealed record Refusal(OpenPlayFailure Failure, string Message);

    /// <summary>What the database has to say about the input before the domain does.</summary>
    private Refusal? Check(BookableCourt court, OpenPlayInput input, DateTimeOffset now)
    {
        if (!court.IsActive || !court.Court.IsActive || !court.CourtSport.Sport.IsActive)
        {
            return new(OpenPlayFailure.Invalid, "That court is not taking bookings for this sport.");
        }

        var today = DateOnly.FromDateTime(VenueClock.LocalNowIn(court.Court.Facility.TimeZone, now).DateTime);

        if (input.StartDate < today)
        {
            return new(OpenPlayFailure.StartsInThePast, "The start date has already passed at the venue.");
        }

        return OutsideHours(court, Days(input.Days), input.StartsAt, input.EndsAt) is string closed
            ? new(OpenPlayFailure.OutsideOpeningHours, closed)
            : null;
    }

    /// <summary>
    /// Whether the hours fit inside the court's opening hours on every checked
    /// day. The court's own hours when it has opted out, otherwise the venue's,
    /// the same rule the booking page uses.
    /// </summary>
    private static string? OutsideHours(BookableCourt court, OpenPlayDays days, TimeOnly startsAt, TimeOnly endsAt)
    {
        foreach (var day in Enum.GetValues<DayOfWeek>().Where(day => days.Includes(day)))
        {
            var (opens, closes) = court.Court.UsesFacilityHours
                ? court.Court.Facility.OperatingHours
                    .Where(hour => hour.DayOfWeek == day)
                    .Select(hour => (hour.OpensAt, hour.ClosesAt))
                    .FirstOrDefault()
                : court.Court.OperatingHours
                    .Where(hour => hour.DayOfWeek == day)
                    .Select(hour => (hour.OpensAt, hour.ClosesAt))
                    .FirstOrDefault();

            if (opens is not TimeOnly opensAt || closes is not TimeOnly closesAt)
            {
                return $"{court.Court.Name} is closed on {day}s.";
            }

            if (startsAt < opensAt || endsAt > closesAt)
            {
                return $"{court.Court.Name} is open {opensAt:h\\:mm tt} to {closesAt:h\\:mm tt} on {day}s. " +
                    "The open play has to fit inside those hours.";
            }
        }

        return null;
    }

    /// <summary>
    /// Everything already holding hours this open play wants, from the later of
    /// its start and the venue's today: live bookings, moves waiting for the
    /// desk, and other published open plays on a clashing part of the floor.
    /// </summary>
    private async Task<IReadOnlyCollection<OpenPlayClash>> ClashesAsync(
        OpenPlay openPlay,
        BookableCourt court,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(VenueClock.LocalNowIn(court.Court.Facility.TimeZone, now).DateTime);
        var firstDate = openPlay.StartDate > today ? openPlay.StartDate : today;
        var live = BookingStatuses.Live;

        var booked = await db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                slot.CourtId == court.CourtId
                && slot.Date >= firstDate
                && (openPlay.EndDate == null || slot.Date <= openPlay.EndDate)
                && live.Contains(slot.Booking.Status)
                && (slot.Booking.Status != BookingStatus.PendingPayment
                    || slot.Booking.ReceiptUrl != null
                    || now < slot.Booking.HoldsUntil))
            .Select(slot => new
            {
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.BookableCourt.CourtSportId,
                slot.BookableCourt.DivisionNumber,
                Customer = db.Users
                    .Where(user => user.Id == slot.Booking.CustomerUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var waiting = await db.BookingUpgradeSlots
            .AsNoTracking()
            .Where(slot =>
                slot.Request.ToBookableCourt.CourtId == court.CourtId
                && slot.Date >= firstDate
                && (openPlay.EndDate == null || slot.Date <= openPlay.EndDate)
                && slot.Request.Status == UpgradeStatus.AwaitingApproval)
            .Select(slot => new
            {
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.Request.ToBookableCourt.CourtSportId,
                slot.Request.ToBookableCourt.DivisionNumber
            })
            .ToListAsync(ct);

        var clashes = booked
            .Where(slot => court.ClashesWith(slot.CourtSportId, slot.DivisionNumber)
                && openPlay.Occupies(slot.Date, slot.StartsAt, slot.EndsAt))
            .Select(slot => new OpenPlayClash(
                "Booking", slot.Date, slot.StartsAt, slot.EndsAt, slot.Customer ?? "A customer"))
            .Concat(waiting
                .Where(slot => court.ClashesWith(slot.CourtSportId, slot.DivisionNumber)
                    && openPlay.Occupies(slot.Date, slot.StartsAt, slot.EndsAt))
                .Select(slot => new OpenPlayClash(
                    "Booking", slot.Date, slot.StartsAt, slot.EndsAt, "A move waiting for the desk")))
            .ToList();

        var others = await db.OpenPlays
            .AsNoTracking()
            .Where(other => other.CourtId == court.CourtId
                && other.Id != openPlay.Id
                && other.PublishedAt != null
                && (other.EndDate == null || other.EndDate >= firstDate))
            .Include(other => other.BookableCourt)
            .ToListAsync(ct);

        foreach (var other in others.Where(other =>
            court.ClashesWith(other.BookableCourt.CourtSportId, other.BookableCourt.DivisionNumber)
            && other.SharesHoursWith(openPlay)))
        {
            // The first date both run on, which is what the desk needs to see.
            var date = Enumerable.Range(0, ClashHorizonDays)
                .Select(offset => firstDate.AddDays(offset))
                .FirstOrDefault(candidate => openPlay.RunsOn(candidate) && other.RunsOn(candidate));

            if (date != default)
            {
                clashes.Add(new OpenPlayClash(
                    "OpenPlay",
                    date,
                    other.StartsAt > openPlay.StartsAt ? other.StartsAt : openPlay.StartsAt,
                    other.EndsAt < openPlay.EndsAt ? other.EndsAt : openPlay.EndsAt,
                    other.Title));
            }
        }

        return [.. clashes.OrderBy(clash => clash.Date).ThenBy(clash => clash.StartsAt).Take(ClashesShown)];
    }

    // ------------------------------------------------------------ the reads

    /// <summary>The venues this person works: the ones they own, and the ones they are on the desk of.</summary>
    private IQueryable<Guid> DeskVenues(Guid userId) =>
        db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant => attendant.UserId == userId && attendant.IsActive))
            .Select(facility => facility.Id);

    /// <summary>
    /// One owner's venues, for the platform admin. The owner is the scope, so an
    /// open play at another owner's venue answers as not found even to an admin
    /// who could open that owner's page: a link to the wrong owner does nothing.
    /// </summary>
    private IQueryable<Guid> OwnerVenues(Guid facilityOwnerId) =>
        db.Facilities
            .Where(facility => facility.FacilityOwnerId == facilityOwnerId)
            .Select(facility => facility.Id);

    /// <summary>The open play, tracked, only if it is at one of these venues.</summary>
    private async Task<OpenPlay?> ForScopeAsync(IQueryable<Guid> venueIds, Guid openPlayId, CancellationToken ct) =>
        await db.OpenPlays.SingleOrDefaultAsync(
            openPlay => openPlay.Id == openPlayId && venueIds.Contains(openPlay.FacilityId),
            ct);

    /// <summary>The bookable court with what the checks read, only if it is at one of these venues.</summary>
    private async Task<BookableCourt?> CourtInScopeAsync(
        IQueryable<Guid> venueIds,
        Guid bookableCourtId,
        CancellationToken ct)
    {
        return await db.BookableCourts
            .AsNoTracking()
            .Include(bookable => bookable.CourtSport).ThenInclude(link => link.Sport)
            .Include(bookable => bookable.Court).ThenInclude(court => court.OperatingHours)
            .Include(bookable => bookable.Court).ThenInclude(court => court.Facility)
                .ThenInclude(facility => facility.OperatingHours)
            .SingleOrDefaultAsync(
                bookable => bookable.Id == bookableCourtId && venueIds.Contains(bookable.Court.FacilityId),
                ct);
    }

    private async Task<DateOnly> VenueTodayAsync(Guid facilityId, DateTimeOffset now, CancellationToken ct)
    {
        var timeZone = await db.Facilities
            .Where(facility => facility.Id == facilityId)
            .Select(facility => facility.TimeZone)
            .SingleAsync(ct);

        return DateOnly.FromDateTime(VenueClock.LocalNowIn(timeZone, now).DateTime);
    }

    /// <summary>
    /// Registrations still holding a spot: confirmed, with the desk, or inside
    /// their payment hold. A hold that ran out with no receipt holds nothing,
    /// the same rule as <see cref="OpenPlayRegistration.HasLapsedAt"/>, asked in SQL.
    /// </summary>
    private Task<int> LiveRegistrationsAsync(Guid openPlayId, DateOnly? after, CancellationToken ct)
    {
        var live = BookingStatuses.Live;
        var now = timeProvider.GetUtcNow();

        return db.OpenPlayRegistrations.CountAsync(
            registration => registration.Session.OpenPlayId == openPlayId
                && (after == null || registration.Session.Date > after)
                && live.Contains(registration.Status)
                && (registration.Status != BookingStatus.PendingPayment
                    || registration.ReceiptUrl != null
                    || now < registration.HoldsUntil),
            ct);
    }

    private async Task<DeskOpenPlay> ProjectOneAsync(Guid openPlayId, CancellationToken ct) =>
        (await ProjectAsync([openPlayId], ct)).Single();

    private async Task<IReadOnlyCollection<DeskOpenPlay>> ProjectAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return [];
        }

        var live = BookingStatuses.Live;

        var rows = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => ids.Contains(openPlay.Id))
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                FacilityName = openPlay.Facility.Name,
                openPlay.Facility.TimeZone,
                OwnerId = openPlay.Facility.FacilityOwnerId,
                CourtName = openPlay.BookableCourt.Court.Name,
                SportKey = openPlay.BookableCourt.CourtSport.Sport.Key,
                SportName = openPlay.BookableCourt.CourtSport.Sport.Name,
                openPlay.BookableCourt.DivisionNumber,
                openPlay.BookableCourt.CourtSport.Divisions,
                // Registered players are the confirmed ones only. A receipt the
                // desk has not looked at yet is waiting, not registered.
                Registrations = db.OpenPlayRegistrations.Count(registration =>
                    registration.Session.OpenPlayId == openPlay.Id
                    && registration.Status == BookingStatus.Confirmed),
                Waiting = db.OpenPlayRegistrations.Count(registration =>
                    registration.Session.OpenPlayId == openPlay.Id
                    && registration.Status == BookingStatus.PendingVerification)
            })
            .ToListAsync(ct);

        var ownerIds = rows.Select(row => row.OwnerId).Distinct().ToArray();
        var now = timeProvider.GetUtcNow();
        var roughToday = DateOnly.FromDateTime(now.UtcDateTime);

        var contracts = await db.FacilityOwnerContracts
            .AsNoTracking()
            .Where(contract => ownerIds.Contains(contract.FacilityOwnerId) && contract.CancelledAt == null)
            .ToListAsync(ct);

        return
        [
            .. rows
                .OrderBy(row => row.OpenPlay.Status == OpenPlayStatus.Ended)
                .ThenBy(row => row.FacilityName)
                .ThenBy(row => row.OpenPlay.Title)
                .Select(row =>
                {
                    var openPlay = row.OpenPlay;
                    var today = DateOnly.FromDateTime(VenueClock.LocalNowIn(row.TimeZone, now).DateTime);

                    // The live term's rate, or the most recent one's when the
                    // venue is between terms. What a player would be charged
                    // if they registered today.
                    var contract = contracts
                        .Where(candidate => candidate.FacilityOwnerId == row.OwnerId)
                        .OrderByDescending(candidate => candidate.Covers(today))
                        .ThenByDescending(candidate => candidate.StartDate)
                        .FirstOrDefault();
                    var platformFee = contract?.PlatformHourlyRate ?? PlatformRates.DefaultHourlyRate;

                    return new DeskOpenPlay(
                        openPlay.Id,
                        openPlay.FacilityId,
                        row.FacilityName,
                        openPlay.BookableCourtId,
                        openPlay.CourtId,
                        row.CourtName,
                        DeskService.UnitLabel(row.SportName, row.DivisionNumber, row.Divisions),
                        row.SportKey,
                        row.SportName,
                        openPlay.Title,
                        openPlay.Level,
                        openPlay.MaxPlayers,
                        openPlay.RegistrationFee,
                        platformFee,
                        openPlay.RegistrationFee + platformFee,
                        openPlay.StartsAt,
                        openPlay.EndsAt,
                        [.. Enum.GetValues<DayOfWeek>().Where(day => openPlay.Days.Includes(day))],
                        openPlay.StartDate,
                        openPlay.EndDate,
                        openPlay.RegistrationCutoffMinutes,
                        openPlay.EarlyBird is { } earlyBird
                            ? new OpenPlayEarlyBirdInput(earlyBird.DiscountKind, earlyBird.DiscountValue, earlyBird.LeadMinutes)
                            : null,
                        openPlay.Status,
                        openPlay.IsSeeded,
                        openPlay.CreatedAt,
                        openPlay.PublishedAt,
                        openPlay.EndedAt,
                        row.Registrations,
                        openPlay.CoverPhotoUrl,
                        row.Waiting);
                })
        ];
    }

    // ------------------------------------------------------------ small parts

    private static OpenPlayDays Days(IReadOnlyCollection<DayOfWeek>? days) =>
        (days ?? []).Aggregate(OpenPlayDays.None, (all, day) => all | OpenPlayDaysExtensions.From(day));

    private static OpenPlayEarlyBird? EarlyBird(OpenPlayEarlyBirdInput? input) =>
        input is null ? null : new OpenPlayEarlyBird(input.DiscountKind, input.DiscountValue, input.LeadMinutes);

    /// <summary>The domain's message without the "(Parameter 'x')" .NET appends.</summary>
    private static string Plain(ArgumentException problem)
    {
        var message = problem.Message;
        var cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);

        return cut < 0 ? message : message[..cut];
    }

    private static Dictionary<string, string?> Describe(OpenPlay openPlay) => new()
    {
        ["title"] = openPlay.Title,
        ["bookableCourtId"] = openPlay.BookableCourtId.ToString(),
        ["level"] = openPlay.Level,
        ["maxPlayers"] = openPlay.MaxPlayers.ToString(CultureInfo.InvariantCulture),
        ["registrationFee"] = openPlay.RegistrationFee.ToString("0.00", CultureInfo.InvariantCulture),
        ["hours"] = $"{openPlay.StartsAt:HH\\:mm}-{openPlay.EndsAt:HH\\:mm}",
        ["days"] = openPlay.Days.ToString(),
        ["dates"] = $"{openPlay.StartDate:yyyy-MM-dd} to {(openPlay.EndDate is { } end ? end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "open")}",
        ["cutoffMinutes"] = openPlay.RegistrationCutoffMinutes.ToString(CultureInfo.InvariantCulture),
        ["earlyBird"] = openPlay.EarlyBird is { } earlyBird
            ? $"{earlyBird.DiscountKind} {earlyBird.DiscountValue.ToString("0.##", CultureInfo.InvariantCulture)}, {earlyBird.LeadMinutes} min"
            : null,
        ["status"] = openPlay.Status
    };
}
