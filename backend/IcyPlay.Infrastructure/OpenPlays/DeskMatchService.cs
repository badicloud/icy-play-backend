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
        MatchResultRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var (sessionId, door) = await MatchAtDoorAsync(actor, matchId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        // The last game often runs past the session's time, so it can still be
        // finished then; not once the desk has ended the session.
        if (door.IsEnded)
        {
            return Fail(OpenPlayRegistrationFailure.SessionOver);
        }

        var refusal = await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var match = session.Matches.Single(candidate => candidate.Id == matchId);
                var refused = OpenPlayLineup.Finish(
                    session, match, actor.UserId!.Value, door.TeamSize,
                    request.Result, request.ScoreA, request.ScoreB, timeProvider.GetUtcNow());

                if (refused is null)
                {
                    Record(actor, AuditAction.OpenPlayMatchFinished, door, match.Number, null,
                        "Match finished: " + Outcome(request) + ".");
                }

                return refused;
            },
            ct);

        return refusal is LineupRefusal refused
            ? Fail(Failure(refused))
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> CorrectResultAsync(
        AuditActor actor,
        Guid matchId,
        MatchResultRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var (sessionId, door) = await MatchAtDoorAsync(actor, matchId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        // Over on the venue's clock: what was played stands as it was.
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
                var was = match.Result is null
                    ? "no result"
                    : Outcome(new MatchResultRequest(match.Result, match.ScoreA, match.ScoreB));
                var refused = OpenPlayLineup.CorrectResult(
                    match, request.Result, request.ScoreA, request.ScoreB, timeProvider.GetUtcNow());

                if (refused is null)
                {
                    Record(actor, AuditAction.OpenPlayMatchResultChanged, door, match.Number, null,
                        "Result changed from " + was + " to " + Outcome(request) + ".");
                }

                return refused;
            },
            ct);

        return refusal is LineupRefusal refused
            ? Fail(Failure(refused))
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<SeriesLeaderboard>> SeriesLeaderboardAsync(
        Guid userId,
        Guid openPlayId,
        CancellationToken ct)
    {
        var venueIds = db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant => attendant.UserId == userId && attendant.IsActive))
            .Select(facility => facility.Id);

        var openPlay = await db.OpenPlays
            .AsNoTracking()
            .Where(candidate => candidate.Id == openPlayId && venueIds.Contains(candidate.FacilityId))
            .Select(candidate => new
            {
                candidate.Title,
                SportName = candidate.BookableCourt.CourtSport.Sport.Name
            })
            .SingleOrDefaultAsync(ct);

        if (openPlay is null)
        {
            return OpenPlayRegistrationResult<SeriesLeaderboard>.Fail(OpenPlayRegistrationFailure.NotFound);
        }

        var sessionIds = db.OpenPlaySessions
            .Where(session => session.OpenPlayId == openPlayId)
            .Select(session => session.Id);

        var matches = await db.OpenPlayMatches
            .AsNoTracking()
            .Include(match => match.Slots)
            .Where(match => sessionIds.Contains(match.SessionId)
                && match.Status == OpenPlayMatchStatus.Finished
                && match.Result != null)
            .ToListAsync(ct);

        // Registered players only, by their account: the same person from one
        // session to the next. Free players are left out.
        var players = await db.OpenPlayParticipants
            .AsNoTracking()
            .Where(participant => sessionIds.Contains(participant.SessionId) && participant.RegistrationId != null)
            .Join(db.OpenPlayRegistrations, participant => participant.RegistrationId, registration => registration.Id,
                (participant, registration) => new
                {
                    participant.Id,
                    participant.SessionId,
                    registration.CustomerUserId,
                    Name = db.Users.Where(user => user.Id == registration.CustomerUserId).Select(user => user.FullName).FirstOrDefault()
                })
            .ToDictionaryAsync(row => row.Id, ct);

        var standings = OpenPlayStandings.Tally(matches)
            .Where(standing => players.ContainsKey(standing.ParticipantId))
            .GroupBy(standing => players[standing.ParticipantId].CustomerUserId)
            .Select(person => new
            {
                Name = person.Select(standing => players[standing.ParticipantId].Name).FirstOrDefault(name => name is not null),
                Sessions = person.Select(standing => players[standing.ParticipantId].SessionId).Distinct().Count(),
                Played = person.Sum(standing => standing.Played),
                Won = person.Sum(standing => standing.Won),
                Drawn = person.Sum(standing => standing.Drawn),
                Lost = person.Sum(standing => standing.Lost)
            })
            .OrderByDescending(person => person.Won)
            .ThenBy(person => person.Lost)
            .ThenByDescending(person => person.Drawn)
            .ThenBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var wins = standings.Select(person => person.Won).ToList();

        return OpenPlayRegistrationResult<SeriesLeaderboard>.Success(new SeriesLeaderboard(
            openPlayId,
            openPlay.Title,
            openPlay.SportName,
            matches.Select(match => match.SessionId).Distinct().Count(),
            [.. standings.Select(person => new SeriesStanding(
                person.Name ?? "A player",
                DeskCheckInService.ShortName(person.Name),
                OpenPlayStandings.RankOf(wins, person.Won),
                person.Sessions,
                person.Played,
                person.Won,
                person.Drawn,
                person.Lost))]));
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

        // Over on the venue's clock: what was played stands as it was.
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

    public async Task<OpenPlayRegistrationResult<MatchBoard>> PlayNextMatchAsync(
        AuditActor actor,
        Guid participantId,
        PlayNextMatchRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var (sessionId, door) = await ParticipantAtDoorAsync(actor, participantId, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        if (door.IsOver)
        {
            return Fail(OpenPlayRegistrationFailure.SessionOver);
        }

        var replacementSession = await db.OpenPlayParticipants
            .Where(participant => participant.Id == request.ReplacementParticipantId)
            .Select(participant => (Guid?)participant.SessionId)
            .SingleOrDefaultAsync(ct);

        if (replacementSession != sessionId)
        {
            return Fail(OpenPlayRegistrationFailure.CannotPlayNextMatch);
        }

        var makingWay = await NameOfAsync(participantId, ct);
        var replacement = await NameOfAsync(request.ReplacementParticipantId, ct);

        var refusal = await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var participant = session.Participants.Single(candidate => candidate.Id == participantId);
                var taking = session.Participants.Single(candidate => candidate.Id == request.ReplacementParticipantId);
                var matchNumber = OpenPlayLineup.NextMatch(session)?.Number;
                var refused = OpenPlayLineup.PlayNextMatch(session, participant, taking, door.TeamSize, timeProvider.GetUtcNow());

                if (refused is null)
                {
                    Record(actor, AuditAction.OpenPlayPlayerPlaysNextMatch, door, matchNumber, makingWay,
                        $"To the end of the line; {replacement} took their seat.");
                }

                return refused;
            },
            ct);

        return refusal is LineupRefusal refused
            ? Fail(Failure(refused))
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> MoveToTeamAsync(
        AuditActor actor,
        Guid participantId,
        MoveToTeamRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

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

        var refusal = await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                var player = session.Participants.Single(candidate => candidate.Id == participantId);
                var matchNumber = OpenPlayLineup.NextMatch(session)?.Number;
                var refused = OpenPlayLineup.MoveToTeam(session, player, request.Team, door.TeamSize, timeProvider.GetUtcNow());

                if (refused is null)
                {
                    Record(actor, AuditAction.OpenPlayPlayerMovedTeam, door, matchNumber, name, $"Moved to Team {request.Team}.");
                }

                return refused;
            },
            ct);

        return refusal is LineupRefusal refused
            ? Fail(Failure(refused))
            : OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(door, ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> EndSessionAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var door = await SessionDoor.FindAsync(db, timeProvider, actor.UserId ?? Guid.Empty, openPlayId, date, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        var sessionId = await db.OpenPlaySessions
            .Where(session => session.OpenPlayId == openPlayId && session.Date == date)
            .Select(session => (Guid?)session.Id)
            .SingleOrDefaultAsync(ct);

        if (sessionId is not Guid id)
        {
            // Nobody has registered, so there is nothing to end.
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        var refusal = await SessionLineups.ApplyAsync(
            db,
            id,
            session =>
            {
                var wasEnded = session.IsEnded;
                var refused = OpenPlayLineup.EndSession(session, actor.UserId!.Value, timeProvider.GetUtcNow());

                if (refused is null && !wasEnded)
                {
                    Record(actor, AuditAction.OpenPlaySessionEnded, door, null, null, "Session ended by the desk.");
                }

                return refused;
            },
            ct);

        if (refusal is LineupRefusal.CourtBusy)
        {
            return Fail(OpenPlayRegistrationFailure.MatchStillPlaying);
        }

        return OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(await DoorAgainAsync(door, actor, ct), ct));
    }

    public async Task<OpenPlayRegistrationResult<MatchBoard>> ReopenSessionAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var door = await SessionDoor.FindAsync(db, timeProvider, actor.UserId ?? Guid.Empty, openPlayId, date, ct);

        if (door is null)
        {
            return Fail(OpenPlayRegistrationFailure.NotFound);
        }

        if (!door.IsEnded || door.TimeIsUp)
        {
            return Fail(OpenPlayRegistrationFailure.CannotReopenSession);
        }

        var sessionId = await db.OpenPlaySessions
            .Where(session => session.OpenPlayId == openPlayId && session.Date == date)
            .Select(session => session.Id)
            .SingleAsync(ct);

        await SessionLineups.ApplyAsync(
            db,
            sessionId,
            session =>
            {
                OpenPlayLineup.ReopenSession(session, door.TeamSize, timeProvider.GetUtcNow());
                Record(actor, AuditAction.OpenPlaySessionReopened, door, null, null, "Session reopened by the desk.");

                return true;
            },
            ct);

        return OpenPlayRegistrationResult<MatchBoard>.Success(await BoardOfAsync(await DoorAgainAsync(door, actor, ct), ct));
    }

    /// <summary>The door read again, after ending or reopening changed it.</summary>
    private async Task<SessionDoor> DoorAgainAsync(SessionDoor door, AuditActor actor, CancellationToken ct) =>
        await SessionDoor.FindAsync(db, timeProvider, actor.UserId ?? Guid.Empty, door.OpenPlay.Id, door.Date, ct) ?? door;

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

        var played = matches
            .Where(match => match.Status == OpenPlayMatchStatus.Finished)
            .SelectMany(match => match.Slots.Select(slot => slot.ParticipantId).Distinct())
            .GroupBy(participantId => participantId)
            .ToDictionary(games => games.Key, games => games.Count());

        var players = (await PlayersAsync(participants, ct)).ToDictionary(
            pair => pair.Key,
            pair => pair.Value with { Played = played.GetValueOrDefault(pair.Key) });

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

        var tally = OpenPlayStandings.Tally(matches);
        var best = OpenPlayStandings.Best(tally).Select(standing => standing.ParticipantId).ToHashSet();
        var wins = tally.Select(standing => standing.Won).ToList();

        var endedAt = sessionId is Guid endedSession
            ? await db.OpenPlaySessions.Where(session => session.Id == endedSession).Select(session => session.EndedAt).SingleAsync(ct)
            : null;

        MatchWinningTeam? winningTeam = null;

        if (OpenPlayLineup.WinningTeam(matches) is var (wonMatch, wonTeam))
        {
            winningTeam = new MatchWinningTeam(
                wonMatch.Number,
                wonTeam,
                [.. wonMatch.Seated
                    .Where(slot => slot.Team == wonTeam)
                    .OrderBy(slot => slot.Position)
                    .Select(slot => players[slot.ParticipantId])]);
        }

        var group = participants
            .OrderBy(participant => participant.CreatedAt)
            .Select(participant => players[participant.Id])
            .ToList();

        var photos = sessionId is Guid photoSession
            ? await DeskResultPhotoService.PhotosAsync(db, photoSession, timeProvider.GetUtcNow(), ct)
            : [];

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
                Team(match, OpenPlayTeam.B),
                match.Result,
                match.ScoreA,
                match.ScoreB))],
            waiting,
            done,
            [.. tally.Select(standing => new MatchStanding(
                players[standing.ParticipantId],
                OpenPlayStandings.RankOf(wins, standing.Won),
                standing.Played,
                standing.Won,
                standing.Drawn,
                standing.Lost,
                best.Contains(standing.ParticipantId)))],
            endedAt,
            door.IsEnded && !door.TimeIsUp,
            winningTeam,
            group,
            photos);
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
        LineupRefusal.Uneven => OpenPlayRegistrationFailure.TeamsUneven,
        LineupRefusal.ResultDoesNotHold => OpenPlayRegistrationFailure.InvalidMatchResult,
        LineupRefusal.NotFinished => OpenPlayRegistrationFailure.MatchNotFinished,
        LineupRefusal.NotInNextMatch or LineupRefusal.ReplacementNotFurtherBack => OpenPlayRegistrationFailure.CannotPlayNextMatch,
        LineupRefusal.SameTeam => OpenPlayRegistrationFailure.CannotMoveTeam,
        _ => OpenPlayRegistrationFailure.MatchNotPlaying
    };

    /// <summary>"Team A won 21–17", "a draw": for the activity log.</summary>
    private static string Outcome(MatchResultRequest request)
    {
        var who = request.Result switch
        {
            OpenPlayMatchResult.TeamA => "Team A won",
            OpenPlayMatchResult.TeamB => "Team B won",
            _ => "a draw"
        };

        return request.ScoreA is int a && request.ScoreB is int b
            ? string.Create(CultureInfo.InvariantCulture, $"{who} {a}–{b}")
            : who;
    }

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
