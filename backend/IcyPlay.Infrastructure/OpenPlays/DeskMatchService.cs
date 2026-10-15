using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// The matches board of an open play session and the desk's buttons on it.
/// The rules are <see cref="OpenPlayLineup"/>'s; this finds the session, checks
/// the person works the venue, saves, records, and reads the board back.
/// </summary>
public sealed class DeskMatchService(
    AppDbContext db,
    IAuditLogger audit,
    TimeProvider timeProvider) : IDeskMatchService
{
    public async Task<OpenPlayRegistrationResult<MatchBoard>> BoardAsync(
        Guid userId,
        Guid openPlayId,
        DateOnly date,
        CancellationToken ct)
    {
        var door = await SessionDoor.FindAsync(db, timeProvider, userId, openPlayId, date, ct);

        return door is null
            ? Fail(OpenPlayRegistrationFailure.NotFound)
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> StartAsync(
        AuditActor actor,
        Guid matchId,
        StartMatchRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var (sessionId, door) = await MatchAtDoorAsync(actor, matchId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        if (door.IsOver)
        {
            return Fail(OpenPlayRegistrationFailure.SessionOver);
        }

        var refusal = await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var match = session.Matches.Single(candidate => candidate.Id == matchId);
                var refused = OpenPlayLineup.Start(
                    session, match, actor.UserId!.Value, door.TeamSize, request.EvenIfShort, timeProvider.GetUtcNow());

                if (refused is null)
                {
                    Record(actor, AuditAction.OpenPlayMatchStarted, door, match.Number, null,
                        match.IsFull(door.TeamSize) ? "Match started." : "Match started short of players.");
                }

                return refused;
            },
            ct);

        return refusal is LineupRefusal refused
            ? Fail(Failure(refused))
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> FinishAsync(
        AuditActor actor,
        Guid matchId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var (sessionId, door) = await MatchAtDoorAsync(actor, matchId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        var refusal = await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var match = session.Matches.Single(candidate => candidate.Id == matchId);
                var refused = OpenPlayLineup.Finish(session, match, actor.UserId!.Value, door.TeamSize, timeProvider.GetUtcNow());

                if (refused is null)
                {
                    Record(actor, AuditAction.OpenPlayMatchFinished, door, match.Number, null, "Match finished.");
                }

                return refused;
            },
            ct);

        return refusal is LineupRefusal refused
            ? Fail(Failure(refused))
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> DoneAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var (sessionId, door) = await ParticipantAtDoorAsync(actor, participantId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        var name = await NameOfAsync(participantId, ct);

        await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var participant = session.Participants.Single(candidate => candidate.Id == participantId);

                if (!participant.IsDone)
                {
                    OpenPlayLineup.Done(session, participant, door.TeamSize, timeProvider.GetUtcNow());
                    Record(actor, AuditAction.OpenPlayPlayerDone, door, null, name, "Done for the day.");
                }

                return true;
            },
            ct);

        return OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> BackInAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var (sessionId, door) = await ParticipantAtDoorAsync(actor, participantId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        if (door.IsOver)
        {
            return Fail(OpenPlayRegistrationFailure.SessionOver);
        }

        var name = await NameOfAsync(participantId, ct);

        await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var participant = session.Participants.Single(candidate => candidate.Id == participantId);

                if (participant.IsDone)
                {
                    OpenPlayLineup.BackIn(session, participant, door.TeamSize, timeProvider.GetUtcNow());
                    Record(actor, AuditAction.OpenPlayPlayerBackIn, door, null, name, "Back in the line.");
                }

                return true;
            },
            ct);

        return OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    // ------------------------------------------------------------ the reads

    private async Task<MatchBoard> BoardOfAsync(SessionDoor door, CancellationToken ct)
    {
        var sessionId = await db.OpenPlaySessions
            .Where(session => session.OpenPlayId == door.OpenPlay.Id && session.Date == door.Date)
            .Select(session => (Guid?)session.Id)
            .SingleOrDefaultAsync(ct);

        var participants = sessionId is Guid id
            ? await db.OpenPlayParticipants.AsNoTracking().Where(participant => participant.SessionId == id).ToListAsync(ct)
            : [];

        var matches = sessionId is Guid matchSession
            ? await db.OpenPlayMatches
                .AsNoTracking()
                .Include(match => match.Slots)
                .Where(match => match.SessionId == matchSession)
                .OrderBy(match => match.Number)
                .ToListAsync(ct)
            : [];

        var players = await PlayersAsync(participants, ct);

        MatchBoardPlayer? At(OpenPlayMatch match, string team, int position)
        {
            var slot = match.Slots.FirstOrDefault(candidate =>
                candidate.LeftAt is null && candidate.Team == team && candidate.Position == position);

            return slot is null ? null : players.GetValueOrDefault(slot.ParticipantId);
        }

        IReadOnlyList<MatchBoardPlayer?> Team(OpenPlayMatch match, string team)
        {
            // As many seats as the team has, or more when it was formed under
            // a larger team size than the one now set.
            var seats = Math.Max(
                door.TeamSize,
                match.Slots.Where(slot => slot.LeftAt is null && slot.Team == team).Select(slot => slot.Position).DefaultIfEmpty(0).Max());

            return [.. Enumerable.Range(1, seats).Select(position => At(match, team, position))];
        }

        var waiting = participants
            .Where(participant => participant.Status == OpenPlayParticipantStatus.Waiting)
            .OrderBy(participant => participant.QueuedAt)
            .Select(participant => players[participant.Id])
            .ToList();

        var done = participants
            .Where(participant => participant.Status == OpenPlayParticipantStatus.Done)
            .OrderBy(participant => participant.DoneAt)
            .Select(participant => players[participant.Id])
            .ToList();

        return new MatchBoard(
            door.OpenPlay.Id,
            door.OpenPlay.Title,
            door.FacilityName,
            door.CourtName,
            door.UnitLabel,
            door.Date,
            door.OpenPlay.StartsAt,
            door.OpenPlay.EndsAt,
            door.SportKey,
            door.SportName,
            door.TeamSize,
            door.IsOver,
            door.OpenPlay.BackgroundPhotoUrl,
            [.. matches.Select(match => new MatchBoardMatch(
                match.Id,
                match.Number,
                match.Status,
                match.StartedAt,
                match.FinishedAt,
                Team(match, OpenPlayTeam.A),
                Team(match, OpenPlayTeam.B)))],
            waiting,
            done);
    }

    /// <summary>Everyone in the line by name: registered players from their account, free ones as the desk typed them.</summary>
    private async Task<Dictionary<Guid, MatchBoardPlayer>> PlayersAsync(
        IReadOnlyCollection<OpenPlayParticipant> participants,
        CancellationToken ct)
    {
        var registrationIds = participants.Where(p => p.RegistrationId is not null).Select(p => p.RegistrationId!.Value).ToList();
        var freeIds = participants.Where(p => p.FreePlayerId is not null).Select(p => p.FreePlayerId!.Value).ToList();

        var registered = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registrationIds.Contains(registration.Id))
            .Select(registration => new
            {
                registration.Id,
                Name = db.Users.Where(user => user.Id == registration.CustomerUserId).Select(user => user.FullName).FirstOrDefault()
            })
            .ToDictionaryAsync(row => row.Id, row => row.Name, ct);

        var free = await db.OpenPlayFreePlayers
            .AsNoTracking()
            .Where(player => freeIds.Contains(player.Id))
            .ToDictionaryAsync(player => player.Id, player => player.FullName, ct);

        return participants.ToDictionary(
            participant => participant.Id,
            participant =>
            {
                var isFree = participant.FreePlayerId is not null;
                var name = isFree
                    ? free.GetValueOrDefault(participant.FreePlayerId!.Value)
                    : registered.GetValueOrDefault(participant.RegistrationId!.Value);

                return new MatchBoardPlayer(
                    participant.Id,
                    name ?? "A player",
                    DeskCheckInService.ShortName(name),
                    isFree);
            });
    }

    private async Task<string?> NameOfAsync(Guid participantId, CancellationToken ct)
    {
        var participant = await db.OpenPlayParticipants.AsNoTracking().SingleAsync(p => p.Id == participantId, ct);

        return (await PlayersAsync([participant], ct))[participantId].FullName;
    }

    private async Task<(Guid SessionId, SessionDoor? Door)> MatchAtDoorAsync(AuditActor actor, Guid matchId, CancellationToken ct)
    {
        var row = await db.OpenPlayMatches
            .AsNoTracking()
            .Where(match => match.Id == matchId)
            .Join(db.OpenPlaySessions, match => match.SessionId, session => session.Id,
                (match, session) => new { session.Id, session.OpenPlayId, session.Date })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? (Guid.Empty, null)
            : (row.Id, await SessionDoor.FindAsync(db, timeProvider, actor.UserId ?? Guid.Empty, row.OpenPlayId, row.Date, ct));
    }

    private async Task<(Guid SessionId, SessionDoor? Door)> ParticipantAtDoorAsync(
        AuditActor actor,
        Guid participantId,
        CancellationToken ct)
    {
        var row = await db.OpenPlayParticipants
            .AsNoTracking()
            .Where(participant => participant.Id == participantId)
            .Join(db.OpenPlaySessions, participant => participant.SessionId, session => session.Id,
                (participant, session) => new { session.Id, session.OpenPlayId, session.Date })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? (Guid.Empty, null)
            : (row.Id, await SessionDoor.FindAsync(db, timeProvider, actor.UserId ?? Guid.Empty, row.OpenPlayId, row.Date, ct));
    }

    private static OpenPlayRegistrationResult<MatchBoard> Fail(OpenPlayRegistrationFailure failure) =>
        OpenPlayRegistrationResult<MatchBoard>.Fail(failure);

    private static OpenPlayRegistrationFailure Failure(LineupRefusal refusal) => refusal switch
    {
        LineupRefusal.AlreadyStarted => OpenPlayRegistrationFailure.MatchAlreadyStarted,
        LineupRefusal.CourtBusy => OpenPlayRegistrationFailure.CourtBusy,
        LineupRefusal.NotNext => OpenPlayRegistrationFailure.NotNextMatch,
        LineupRefusal.TeamEmpty => OpenPlayRegistrationFailure.TeamEmpty,
        LineupRefusal.Short => OpenPlayRegistrationFailure.MatchShort,
        _ => OpenPlayRegistrationFailure.MatchNotPlaying
    };

    /// <summary>Against the open play, so it shows in the owner's activity.</summary>
    private void Record(AuditActor actor, string action, SessionDoor door, int? matchNumber, string? player, string reason) =>
        audit.RecordEvent(
            actor,
            action,
            AuditEntityType.OpenPlay,
            door.OpenPlay.Id,
            new Dictionary<string, string?>
            {
                ["date"] = door.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["match"] = matchNumber?.ToString(CultureInfo.InvariantCulture),
                ["player"] = player
            },
            reason);
}
