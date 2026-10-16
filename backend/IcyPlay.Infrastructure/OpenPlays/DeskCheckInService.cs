using System.Data;
using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Door = IcyPlay.Infrastructure.OpenPlays.SessionDoor;

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
                door.IsEnded
                    ? "This session has been ended, so check-in is closed."
                    : $"Check-in for this session opens at {door.OpenPlay.CheckInOpensAt(date):h:mm tt} and closes when it ends.",
                player);
        }

        registration.CheckIn(actor.UserId!.Value, timeProvider.GetUtcNow());
        Record(actor, AuditAction.OpenPlayCheckedIn, registration, "Checked in by scanning the player's QR.");
        await db.SaveChangesAsync(ct);
        await JoinLineAsync(door, registration.SessionId, registration.Id, null, ct);

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

        if (registration is null)
        {
            // Not a registration: a free player, checked in the same way.
            return await ChangeFreeCheckInAsync(actor, registrationId, request, undo: false, ct);
        }

        if (door is null)
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
            await JoinLineAsync(door, registration.SessionId, registration.Id, null, ct);
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

        if (registration is null)
        {
            return await ChangeFreeCheckInAsync(actor, registrationId, request, undo: true, ct);
        }

        if (door is null)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound);
        }

        // Over on the venue's clock: who came stays on record.
        if (door.IsOver)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.SessionOver);
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
            await LeaveLineAsync(door, registration.SessionId, registration.Id, null, ct);
        }

        return OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<CheckInRoster>> AddFreePlayerAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        AddFreePlayerRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var door = await DoorAsync(actor.UserId ?? Guid.Empty, openPlayId, date, ct);

        if (door is null)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound);
        }

        if (door.IsOver)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.SessionOver);
        }

        var name = request.FullName?.Trim();
        var mobile = string.IsNullOrWhiteSpace(request.Mobile) ? null : request.Mobile.Trim();

        if (string.IsNullOrEmpty(name)
            || name.Length > OpenPlayFreePlayer.NameLimit
            || mobile is { Length: > OpenPlayFreePlayer.MobileLimit })
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.InvalidFreePlayer);
        }

        var now = timeProvider.GetUtcNow();
        Guid addedId;
        Guid addedTo;
        bool checkedIn;

        // Counting the spots and taking one happen together, as they do when a
        // player registers: a player joining online at the same moment must
        // not be sold the spot the desk is filling.
        await using (var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var session = await db.OpenPlaySessions
                .Include(candidate => candidate.Registrations)
                .Include(candidate => candidate.FreePlayers)
                .SingleOrDefaultAsync(candidate => candidate.OpenPlayId == openPlayId && candidate.Date == date, ct);

            if (session is null)
            {
                session = new OpenPlaySession(openPlayId, date, now);
                db.OpenPlaySessions.Add(session);
            }

            if (session.FreePlayers.Count >= door.OpenPlay.FreeSpots)
            {
                return Fail<CheckInRoster>(OpenPlayRegistrationFailure.FreeSpotsUsed);
            }

            if (session.SpotsLeftAt(now, door.OpenPlay.MaxPlayers) == 0)
            {
                return Fail<CheckInRoster>(OpenPlayRegistrationFailure.Full);
            }

            var player = session.AddFreePlayer(
                door.OpenPlay.FreeSpots,
                door.OpenPlay.MaxPlayers,
                name,
                mobile,
                actor.UserId!.Value,
                now);

            // Somebody the desk adds while the door is open is standing at it.
            if (door.IsOpen)
            {
                player.CheckIn(actor.UserId!.Value, now);
            }

            db.OpenPlayFreePlayers.Add(player);
            RecordFree(actor, AuditAction.OpenPlayFreePlayerAdded, door, player, "Put in for free by the desk.");

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            addedId = player.Id;
            addedTo = player.SessionId;
            checkedIn = player.IsCheckedIn;
        }

        // Checked in as they were added: into the line for matches.
        if (checkedIn)
        {
            await JoinLineAsync(door, addedTo, null, addedId, ct);
        }

        return OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<CheckInRoster>> RemoveFreePlayerAsync(
        AuditActor actor,
        Guid freePlayerId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var (player, door) = await FreePlayerAtDoorAsync(actor, freePlayerId, ct);

        if (player is null || door is null)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound);
        }

        if (door.IsOver)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.SessionOver);
        }

        // Somebody who has been on court stays on record with their matches:
        // the desk marks them done instead.
        var played = await db.OpenPlayParticipants
            .AnyAsync(participant => participant.FreePlayerId == player.Id
                && db.OpenPlayMatchSlots.Any(slot => slot.ParticipantId == participant.Id
                    && db.OpenPlayMatches.Any(match => match.Id == slot.MatchId
                        && (match.Status == OpenPlayMatchStatus.Playing || match.Status == OpenPlayMatchStatus.Finished))), ct);

        if (played)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.FreePlayerHasPlayed);
        }

        var sessionId = player.SessionId;
        var playerId = player.Id;
        await LeaveLineAsync(door, sessionId, null, playerId, ct);

        var gone = await db.OpenPlayFreePlayers.SingleAsync(candidate => candidate.Id == playerId, ct);
        RecordFree(actor, AuditAction.OpenPlayFreePlayerRemoved, door, gone, "Taken back out by the desk.");
        db.OpenPlayFreePlayers.Remove(gone);
        await db.SaveChangesAsync(ct);

        return OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    /// <summary>
    /// Checks a free player in by hand, or takes it back, with the venue's
    /// code — the same rule as a registered player checked in without a QR.
    /// </summary>
    private async Task<OpenPlayRegistrationResult<CheckInRoster>> ChangeFreeCheckInAsync(
        AuditActor actor,
        Guid freePlayerId,
        CheckInCodeRequest request,
        bool undo,
        CancellationToken ct)
    {
        var (player, door) = await FreePlayerAtDoorAsync(actor, freePlayerId, ct);

        if (player is null || door is null)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.NotFound);
        }

        // Over on the venue's clock: who came stays on record.
        if (undo && door.IsOver)
        {
            return Fail<CheckInRoster>(OpenPlayRegistrationFailure.SessionOver);
        }

        if (await CodeRefusalAsync(actor, door, request.Code, ct) is OpenPlayRegistrationFailure refused)
        {
            return Fail<CheckInRoster>(refused);
        }

        var now = timeProvider.GetUtcNow();

        if (undo)
        {
            if (player.IsCheckedIn)
            {
                player.UndoCheckIn(now);
                RecordFree(actor, AuditAction.OpenPlayCheckInUndone, door, player, "Check-in taken back.");
                await db.SaveChangesAsync(ct);
                await LeaveLineAsync(door, player.SessionId, null, player.Id, ct);
            }
        }
        else
        {
            if (!door.IsOpen)
            {
                return Fail<CheckInRoster>(OpenPlayRegistrationFailure.CheckInClosed);
            }

            if (!player.IsCheckedIn)
            {
                player.CheckIn(actor.UserId!.Value, now);
                RecordFree(actor, AuditAction.OpenPlayCheckedIn, door, player, "Checked in by hand from the roster.");
                await db.SaveChangesAsync(ct);
                await JoinLineAsync(door, player.SessionId, null, player.Id, ct);
            }
        }

        return OpenPlayRegistrationResult<CheckInRoster>.Success(await RosterOfAsync(door, ct));
    }

    // ------------------------------------------------------------ the reads

    private static OpenPlayRegistrationResult<T> Fail<T>(OpenPlayRegistrationFailure failure) =>
        OpenPlayRegistrationResult<T>.Fail(failure);

    /// <summary>
    /// The session, only if it is a published open play at a venue this person
    /// works, on a date it runs and that has not been cancelled.
    /// </summary>
    /// <summary>Somebody has checked in: into the line for matches, and the first open seat.</summary>
    private Task JoinLineAsync(Door door, Guid sessionId, Guid? registrationId, Guid? freePlayerId, CancellationToken ct) =>
        SessionLineups.ApplyAsync(
            db,
            sessionId,
            session => OpenPlayLineup.Join(session, registrationId, freePlayerId, door.TeamSize, timeProvider.GetUtcNow()),
            ct);

    /// <summary>A check-in taken back: out of the line, and the line closes up.</summary>
    private Task LeaveLineAsync(Door door, Guid sessionId, Guid? registrationId, Guid? freePlayerId, CancellationToken ct) =>
        SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var participant = session.Participants.FirstOrDefault(candidate =>
                    (registrationId is not null && candidate.RegistrationId == registrationId)
                    || (freePlayerId is not null && candidate.FreePlayerId == freePlayerId));

                return participant is null
                    || OpenPlayLineup.Withdraw(session, participant, door.TeamSize, timeProvider.GetUtcNow());
            },
            ct);

    private Task<Door?> DoorAsync(Guid userId, Guid openPlayId, DateOnly date, CancellationToken ct) =>
        SessionDoor.FindAsync(db, timeProvider, userId, openPlayId, date, ct);

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

    private async Task<(OpenPlayFreePlayer? Player, Door? Door)> FreePlayerAtDoorAsync(
        AuditActor actor,
        Guid freePlayerId,
        CancellationToken ct)
    {
        var player = await db.OpenPlayFreePlayers
            .Include(candidate => candidate.Session)
            .SingleOrDefaultAsync(candidate => candidate.Id == freePlayerId, ct);

        if (player is null)
        {
            return (null, null);
        }

        var door = await DoorAsync(actor.UserId ?? Guid.Empty, player.Session.OpenPlayId, player.Session.Date, ct);

        return (player, door);
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

        var free = await db.OpenPlayFreePlayers
            .AsNoTracking()
            .Where(player => player.Session.OpenPlayId == door.OpenPlay.Id && player.Session.Date == door.Date)
            .Select(player => new { player.Id, player.FullName, player.Mobile, player.CheckedInAt })
            .ToListAsync(ct);

        // Everybody holding a spot, the ones still paying included, for
        // whether there is room for one more.
        var now = timeProvider.GetUtcNow();
        var registrations = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.Session.OpenPlayId == door.OpenPlay.Id
                && registration.Session.Date == door.Date)
            .ToListAsync(ct);
        var spotsTaken = registrations.Count(registration => registration.HoldsSpotAt(now)) + free.Count;
        var (byRegistration, byFreePlayer) = await LineupsAsync(door, ct);

        var players = rows
            .Select(row => new CheckInPlayer(
                row.Id,
                row.Name ?? "A player",
                ShortName(row.Name),
                row.CheckedInAt,
                Lineup: byRegistration.GetValueOrDefault(row.Id)))
            .Concat(free.Select(player => new CheckInPlayer(
                player.Id,
                player.FullName,
                ShortName(player.FullName),
                player.CheckedInAt,
                IsFree: true,
                player.Mobile,
                byFreePlayer.GetValueOrDefault(player.Id))))
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
            players,
            door.OpenPlay.FreeSpots,
            free.Count,
            !door.IsOver && free.Count < door.OpenPlay.FreeSpots && spotsTaken < door.OpenPlay.MaxPlayers);
    }

    /// <summary>
    /// Where each checked-in player is in the session's matches, by their
    /// registration or free player id: on court, in a match to come, waiting
    /// or done, and how many games they have had.
    /// </summary>
    private async Task<(Dictionary<Guid, CheckInLineup> ByRegistration, Dictionary<Guid, CheckInLineup> ByFreePlayer)> LineupsAsync(
        Door door,
        CancellationToken ct)
    {
        var sessionId = await db.OpenPlaySessions
            .Where(session => session.OpenPlayId == door.OpenPlay.Id && session.Date == door.Date)
            .Select(session => (Guid?)session.Id)
            .SingleOrDefaultAsync(ct);

        if (sessionId is not Guid id)
        {
            return ([], []);
        }

        var participants = await db.OpenPlayParticipants.AsNoTracking().Where(participant => participant.SessionId == id).ToListAsync(ct);
        var matches = await db.OpenPlayMatches
            .AsNoTracking()
            .Include(match => match.Slots)
            .Where(match => match.SessionId == id)
            .ToListAsync(ct);

        CheckInLineup LineupOf(OpenPlayParticipant participant)
        {
            var seatedIn = matches
                .Where(match => match.Status != OpenPlayMatchStatus.Finished
                    && match.Slots.Any(slot => slot.ParticipantId == participant.Id && slot.LeftAt is null))
                .MinBy(match => match.Number);
            var played = matches.Count(match => match.Status == OpenPlayMatchStatus.Finished
                && match.Slots.Any(slot => slot.ParticipantId == participant.Id));
            var hasPlayed = matches.Any(match => match.HasStarted
                && match.Slots.Any(slot => slot.ParticipantId == participant.Id));

            var state = participant.IsDone
                ? "Done"
                : seatedIn?.IsPlaying == true
                    ? "Playing"
                    : seatedIn is not null ? "InMatch" : "Waiting";

            return new CheckInLineup(state, participant.IsDone ? null : seatedIn?.Number, played, hasPlayed);
        }

        return (
            participants.Where(participant => participant.RegistrationId is not null)
                .ToDictionary(participant => participant.RegistrationId!.Value, LineupOf),
            participants.Where(participant => participant.FreePlayerId is not null)
                .ToDictionary(participant => participant.FreePlayerId!.Value, LineupOf));
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

    /// <summary>
    /// A free player in the trail, against the open play so it shows in the
    /// owner's activity: the name, the date, and whether they were checked in.
    /// A removed free player leaves only this behind.
    /// </summary>
    private void RecordFree(AuditActor actor, string action, Door door, OpenPlayFreePlayer player, string reason) =>
        audit.RecordEvent(
            actor,
            action,
            AuditEntityType.OpenPlay,
            door.OpenPlay.Id,
            new Dictionary<string, string?>
            {
                ["freePlayerId"] = player.Id.ToString(),
                ["name"] = player.FullName,
                ["date"] = door.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["checkedInAt"] = player.CheckedInAt?.ToString("O")
            },
            reason);

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
