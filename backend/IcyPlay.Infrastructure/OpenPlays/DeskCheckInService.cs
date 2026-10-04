using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// The door of an open play session. A scan is answered on the server: the
/// QR's token finds the one registration it was made for, and that must be a
/// confirmed registration on this very session (this venue, this open play,
/// this date), inside its check-in window on the venue's clock. Once it has
/// checked its player in, the same QR only says so.
/// </summary>
public sealed class DeskCheckInService(
    AppDbContext db,
    IAuditLogger audit,
    TimeProvider timeProvider) : IDeskCheckInService
{
    public async Task<OpenPlayRegistrationResult<CheckInRoster>> RosterAsync(
        Guid userId,
        Guid openPlayId,
        DateOnly date,
        CancellationToken ct)
    {
        var door = await DoorAsync(userId, openPlayId, date, ct);

        return door is null
            ? Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound)
            : OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<CheckInScanResult>> ScanAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        CheckInScanRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var door = await DoorAsync(actor.UserId ?? Guid.Empty, openPlayId, date, ct);

        if (door is null)
        {
            return Fail<CheckInScanResult>(OpenPlayRegistrationFailure.NotFound);
        }

        async Task<OpenPlayRegistrationResult<CheckInScanResult>> Answer(
            string outcome,
            string message,
            CheckInPlayer? player = null) =>
            OpenPlayRegistrationResult<CheckInScanResult>.Success(
                new CheckInScanResult(outcome, message, player, await RosterOfAsync(door, ct)));

        var token = CheckInPass.TokenFrom(request.Scanned);

        if (token is null)
        {
            return await Answer(CheckInOutcome.NotAPass, "That is not an IcyPlay check-in QR.");
        }

        var registration = await db.OpenPlayRegistrations
            .Include(candidate => candidate.Session)
            .SingleOrDefaultAsync(candidate => candidate.CheckInToken == token, ct);

        if (registration is null)
        {
            return await Answer(CheckInOutcome.UnknownPass, "This QR is not valid.");
        }

        // The QR is for one registration, so it must be this door's: the same
        // open play (and so the same venue) on the same date.
        if (registration.Session.OpenPlayId != openPlayId || registration.Session.Date != date)
        {
            return await Answer(CheckInOutcome.WrongSession, await ElsewhereAsync(registration, door, ct));
        }

        var name = await NameAsync(registration.CustomerUserId, ct);
        var player = Player(registration, name);

        if (!registration.IsRegistered)
        {
            return await Answer(
                CheckInOutcome.NotRegistered,
                $"{name}'s registration for this session was cancelled.",
                player);
        }

        if (registration.IsCheckedIn)
        {
            return await Answer(
                CheckInOutcome.AlreadyCheckedIn,
                $"This QR has been used: {name} checked in at {VenueTime(door, registration.CheckedInAt!.Value):h:mm tt}.",
                player);
        }

        if (!door.IsOpen)
        {
            return await Answer(
                CheckInOutcome.WindowClosed,
                $"Check-in for this session opens at {door.OpenPlay.CheckInOpensAt(date):h:mm tt} and closes when it ends.",
                player);
        }

        registration.CheckIn(actor.UserId!.Value, timeProvider.GetUtcNow());
        Record(actor, AuditAction.OpenPlayCheckedIn, registration, "Checked in by scanning the player's QR.");
        await db.SaveChangesAsync(ct);

        return await Answer(CheckInOutcome.CheckedIn, $"{name} is checked in.", Player(registration, name));
    }

    public async Task<OpenPlayRegistrationResult<CheckInRoster>> CheckInAsync(
        AuditActor actor,
        Guid registrationId,
        CheckInCodeRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var (registration, door) = await RegistrationAtDoorAsync(actor, registrationId, ct);

        if (registration is null || door is null)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound);
        }

        if (await CodeRefusalAsync(actor, door, request.Code, ct) is OpenPlayRegistrationFailure refused)
        {
            return Fail<CheckInRoster>(refused);
        }

        if (!door.IsOpen)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.CheckInClosed);
        }

        if (!registration.IsRegistered)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotRegistered);
        }

        if (!registration.IsCheckedIn)
        {
            registration.CheckIn(actor.UserId!.Value, timeProvider.GetUtcNow());
            Record(actor, AuditAction.OpenPlayCheckedIn, registration, "Checked in by hand from the roster.");
            await db.SaveChangesAsync(ct);
        }

        return OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<CheckInRoster>> UndoAsync(
        AuditActor actor,
        Guid registrationId,
        CheckInCodeRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var (registration, door) = await RegistrationAtDoorAsync(actor, registrationId, ct);

        if (registration is null || door is null)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound);
        }

        if (await CodeRefusalAsync(actor, door, request.Code, ct) is OpenPlayRegistrationFailure refused)
        {
            return Fail<CheckInRoster>(refused);
        }

        if (registration.IsCheckedIn)
        {
            registration.UndoCheckIn(timeProvider.GetUtcNow());
            Record(actor, AuditAction.OpenPlayCheckInUndone, registration, "Check-in taken back.");
            await db.SaveChangesAsync(ct);
        }

        return OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    // ------------------------------------------------------------ the reads

    private sealed record Door(
        OpenPlay OpenPlay,
        DateOnly Date,
        string FacilityName,
        string CourtName,
        string UnitLabel,
        string TimeZone,
        bool IsOpen);

    private static OpenPlayRegistrationResult<T> Fail<T>(OpenPlayRegistrationFailure failure) =>
        OpenPlayRegistrationResult<T>.Fail(failure);

    /// <summary>
    /// The session, only if it is a published open play at a venue this person
    /// works, on a date it runs and that has not been cancelled.
    /// </summary>
    private async Task<Door?> DoorAsync(Guid userId, Guid openPlayId, DateOnly date, CancellationToken ct)
    {
        var venueIds = db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant => attendant.UserId == userId && attendant.IsActive))
            .Select(facility => facility.Id);

        var row = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlay.Id == openPlayId
                && openPlay.PublishedAt != null
                && venueIds.Contains(openPlay.FacilityId))
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                FacilityName = openPlay.Facility.Name,
                openPlay.Facility.TimeZone,
                CourtName = openPlay.BookableCourt.Court.Name,
                SportName = openPlay.BookableCourt.CourtSport.Sport.Name,
                openPlay.BookableCourt.DivisionNumber,
                openPlay.BookableCourt.CourtSport.Divisions,
                Cancelled = db.OpenPlaySessions.Any(session =>
                    session.OpenPlayId == openPlay.Id && session.Date == date && session.CancelledAt != null)
            })
            .SingleOrDefaultAsync(ct);

        if (row is null || row.Cancelled || !row.OpenPlay.RunsOn(date))
        {
            return null;
        }

        var venueNow = VenueClock.LocalNowIn(row.TimeZone, timeProvider.GetUtcNow()).DateTime;

        return new Door(
            row.OpenPlay,
            date,
            row.FacilityName,
            row.CourtName,
            DeskService.UnitLabel(row.SportName, row.DivisionNumber, row.Divisions),
            row.TimeZone,
            row.OpenPlay.IsCheckInOpen(date, venueNow));
    }

    private async Task<(OpenPlayRegistration? Registration, Door? Door)> RegistrationAtDoorAsync(
        AuditActor actor,
        Guid registrationId,
        CancellationToken ct)
    {
        var registration = await db.OpenPlayRegistrations
            .Include(candidate => candidate.Session)
            .SingleOrDefaultAsync(candidate => candidate.Id == registrationId, ct);

        if (registration is null)
        {
            return (null, null);
        }

        var door = await DoorAsync(
            actor.UserId ?? Guid.Empty,
            registration.Session.OpenPlayId,
            registration.Session.Date,
            ct);

        return (registration, door);
    }

    private async Task<CheckInRoster> RosterOfAsync(Door door, CancellationToken ct)
    {
        var rows = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.Session.OpenPlayId == door.OpenPlay.Id
                && registration.Session.Date == door.Date
                && registration.Status == BookingStatus.Confirmed)
            .Select(registration => new
            {
                registration.Id,
                registration.CheckedInAt,
                Name = db.Users
                    .Where(user => user.Id == registration.CustomerUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var players = rows
            .Select(row => new CheckInPlayer(row.Id, row.Name ?? "A player", ShortName(row.Name), row.CheckedInAt))
            .OrderBy(player => player.CheckedInAt is null ? 1 : 0)
            .ThenByDescending(player => player.CheckedInAt)
            .ThenBy(player => player.FullName)
            .ToList();

        return new CheckInRoster(
            door.OpenPlay.Id,
            door.OpenPlay.Title,
            door.FacilityName,
            door.CourtName,
            door.UnitLabel,
            door.Date,
            door.OpenPlay.StartsAt,
            door.OpenPlay.EndsAt,
            door.OpenPlay.CheckInOpensAt(door.Date),
            door.IsOpen,
            door.OpenPlay.MaxPlayers,
            players.Count,
            players.Count(player => player.CheckedInAt is not null),
            players);
    }

    /// <summary>
    /// Which session a QR from somewhere else is for, so the desk can send the
    /// player the right way. Named only when it is at this same venue: another
    /// venue's open play is not this desk's business.
    /// </summary>
    private async Task<string> ElsewhereAsync(OpenPlayRegistration registration, Door door, CancellationToken ct)
    {
        var other = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlay.Id == registration.Session.OpenPlayId)
            .Select(openPlay => new { openPlay.Title, openPlay.FacilityId, openPlay.StartsAt })
            .SingleAsync(ct);

        if (other.FacilityId != door.OpenPlay.FacilityId)
        {
            return "This QR is for an open play at another venue.";
        }

        var when = registration.Session.Date.ToDateTime(other.StartsAt);

        return $"This QR is for {other.Title} on {when:ddd, MMM d} at {when:h:mm tt}, not this session.";
    }

    /// <summary>
    /// Checks the venue's code for anything done at the door without a QR, or
    /// null when it is right. A wrong one is counted and saved at once, so a
    /// run of guesses locks it even though nothing else is changed.
    /// </summary>
    private async Task<OpenPlayRegistrationFailure?> CodeRefusalAsync(
        AuditActor actor,
        Door door,
        string? code,
        CancellationToken ct)
    {
        var ownerId = await db.Facilities
            .Where(facility => facility.Id == door.OpenPlay.FacilityId)
            .Select(facility => facility.FacilityOwnerId)
            .SingleAsync(ct);
        var owner = await db.FacilityOwners.SingleAsync(candidate => candidate.Id == ownerId, ct);
        var now = timeProvider.GetUtcNow();
        var wasLocked = owner.CheckInCodeLockedUntil is DateTimeOffset until && now < until;

        var verdict = owner.TryOpenPlayCheckInCode(code?.Trim(), now);

        switch (verdict)
        {
            case CheckInCodeVerdict.Accepted:
                // A right code clears the count of wrong ones.
                await db.SaveChangesAsync(ct);
                return null;
            case CheckInCodeVerdict.NotSet:
                return OpenPlayRegistrationFailure.CheckInCodeNotSet;
            case CheckInCodeVerdict.Wrong:
                await db.SaveChangesAsync(ct);
                return OpenPlayRegistrationFailure.WrongCheckInCode;
            default:
                if (wasLocked)
                {
                    return OpenPlayRegistrationFailure.CheckInCodeLocked;
                }

                // Locked by this very try: worth a line in the trail.
                audit.RecordEvent(
                        actor,
                        AuditAction.OpenPlayCheckInCodeLocked,
                        AuditEntityType.FacilityOwner,
                        owner.Id,
                        new Dictionary<string, string?>
                        {
                            ["lockedUntil"] = owner.CheckInCodeLockedUntil?.ToString("O")
                        },
                        "Too many wrong open play check-in codes.");

                await db.SaveChangesAsync(ct);
                return OpenPlayRegistrationFailure.CheckInCodeLocked;
        }
    }

    private async Task<string> NameAsync(Guid userId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.FullName)
            .FirstOrDefaultAsync(ct) ?? "This player";

    private static CheckInPlayer Player(OpenPlayRegistration registration, string name) =>
        new(registration.Id, name, ShortName(name), registration.CheckedInAt);

    /// <summary>
    /// "Juan dela Cruz" to "Juan C.": the first name and the last name's
    /// initial, for a screen anybody walking past can read.
    /// </summary>
    internal static string ShortName(string? fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length switch
        {
            0 => "Player",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}."
        };
    }

    private static DateTime VenueTime(Door door, DateTimeOffset moment) =>
        VenueClock.LocalNowIn(door.TimeZone, moment).DateTime;

    private void Record(AuditActor actor, string action, OpenPlayRegistration registration, string reason) =>
        audit.RecordEvent(
            actor,
            action,
            AuditEntityType.OpenPlayRegistration,
            registration.Id,
            new Dictionary<string, string?>
            {
                ["checkedInAt"] = registration.CheckedInAt?.ToString("O")
            },
            reason);
}
