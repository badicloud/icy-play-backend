namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// How a session's matches are made from the players who have checked in.
/// Works on one session with its participants, matches and seats loaded, and
/// saves nothing: the caller does, in one go.
///
/// The rules, as the venue asked for them:
/// <list type="bullet">
/// <item>Players line up in the order they checked in.</item>
/// <item>The matches not yet started always follow the line: its first
/// players fill the next match, Team A before Team B, seat by seat, then the
/// match after, and a new one once every earlier one is full. So when a
/// player leaves, everyone behind them moves up. A match under way takes
/// nobody new.</item>
/// <item>One court, so one match at a time, and the next one in order.</item>
/// <item>A finished match's players go back to the end of the line, in the
/// order they sat, and fill whatever comes next.</item>
/// <item>A player the desk marks done leaves the line. Mid-game, the front of
/// the line takes their seat; before it, the line closes up behind them.</item>
/// </list>
/// </summary>
public static class OpenPlayLineup
{
    /// <summary>
    /// A player who has just checked in. Into the line, then into the first
    /// open seat. Checking in again — after an undo — puts them back in line.
    /// </summary>
    public static OpenPlayParticipant Join(
        OpenPlaySession session,
        Guid? registrationId,
        Guid? freePlayerId,
        int teamSize,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        var participant = session.Participants.FirstOrDefault(candidate =>
            (registrationId is not null && candidate.RegistrationId == registrationId)
            || (freePlayerId is not null && candidate.FreePlayerId == freePlayerId));

        if (participant is null)
        {
            participant = new OpenPlayParticipant(session.Id, registrationId, freePlayerId, EndOfLine(session, now));
            session.Participants.Add(participant);
        }
        else if (participant.IsDone)
        {
            participant.BackToTheLine(EndOfLine(session, now), now);
        }

        Fill(session, teamSize, now);

        return participant;
    }

    /// <summary>
    /// The desk says a player is done for the day: tired, hurt or leaving.
    /// </summary>
    public static void Done(OpenPlaySession session, OpenPlayParticipant participant, int teamSize, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(participant);

        if (participant.IsDone)
        {
            return;
        }

        LeaveSeat(session, participant, now);
        participant.MarkDone(now);
        Fill(session, teamSize, now);
    }

    /// <summary>
    /// A check-in taken back, or a free player taken out. Somebody who never
    /// got on court leaves no trace; somebody who did stays on record as done,
    /// so the matches they played still say who played them.
    /// </summary>
    /// <returns>True when nothing of them is left and the caller can forget them.</returns>
    public static bool Withdraw(OpenPlaySession session, OpenPlayParticipant participant, int teamSize, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(participant);

        LeaveSeat(session, participant, now);

        var played = HasPlayed(session, participant);

        if (played)
        {
            participant.MarkDone(now);
        }
        else
        {
            session.Participants.Remove(participant);
        }

        Fill(session, teamSize, now);

        return !played;
    }

    /// <summary>Back in after being done: to the end of the line.</summary>
    public static void BackIn(OpenPlaySession session, OpenPlayParticipant participant, int teamSize, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(participant);

        if (!participant.IsDone)
        {
            return;
        }

        participant.BackToTheLine(EndOfLine(session, now), now);
        Fill(session, teamSize, now);
    }

    /// <summary>Whether this player has been on court in a match that started.</summary>
    public static bool HasPlayed(OpenPlaySession session, OpenPlayParticipant participant) =>
        session.Matches.Any(match =>
            match.HasStarted && match.Slots.Any(slot => slot.ParticipantId == participant.Id));

    /// <summary>
    /// Starts a match. The court has to be free and it has to be the next
    /// match in order. A match short of players starts only when the desk
    /// says so, and never with an empty team.
    /// </summary>
    public static LineupRefusal? Start(
        OpenPlaySession session,
        OpenPlayMatch match,
        Guid startedByUserId,
        int teamSize,
        bool evenIfShort,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(match);

        if (match.HasStarted)
        {
            return LineupRefusal.AlreadyStarted;
        }

        if (session.Matches.Any(other => other.IsPlaying))
        {
            return LineupRefusal.CourtBusy;
        }

        var nextUp = session.Matches.Where(other => !other.HasStarted).MinBy(other => other.Number);

        if (nextUp != match)
        {
            return LineupRefusal.NotNext;
        }

        if (match.SeatedOn(OpenPlayTeam.A) == 0 || match.SeatedOn(OpenPlayTeam.B) == 0)
        {
            return LineupRefusal.TeamEmpty;
        }

        if (!match.IsFull(teamSize) && !evenIfShort)
        {
            return LineupRefusal.Short;
        }

        match.Start(startedByUserId, now);

        return null;
    }

    /// <summary>
    /// Ends a match. Its players go to the back of the line in the order they
    /// sat — Team A's first — and the line fills what comes next.
    /// </summary>
    public static LineupRefusal? Finish(
        OpenPlaySession session,
        OpenPlayMatch match,
        Guid finishedByUserId,
        int teamSize,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(match);

        if (!match.IsPlaying)
        {
            return LineupRefusal.NotPlaying;
        }

        match.Finish(finishedByUserId, now);

        var offCourt = match.Seated
            .OrderBy(slot => slot.Team)
            .ThenBy(slot => slot.Position)
            .Select(slot => session.Participants.Single(participant => participant.Id == slot.ParticipantId))
            .ToList();

        // A tick apart, so they keep the order they sat in.
        var back = EndOfLine(session, now);

        for (var index = 0; index < offCourt.Count; index++)
        {
            offCourt[index].BackToTheLine(back.AddTicks(index), now);
        }

        Fill(session, teamSize, now);

        return null;
    }

    /// <summary>
    /// Seats the front of the line into every match not yet started, in order,
    /// Team A before Team B; makes a new match when they are all full; drops a
    /// trailing match left with nobody in it.
    /// </summary>
    public static void Fill(OpenPlaySession session, int teamSize, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);

        var line = new Queue<OpenPlayParticipant>(Line(session));
        var upcoming = session.Matches.Where(match => !match.HasStarted).OrderBy(match => match.Number).ToList();

        // Seated again from the line, so the matches still to play always
        // follow it: somebody leaving moves everybody behind them up.
        foreach (var match in upcoming)
        {
            foreach (var slot in match.Slots.ToList())
            {
                session.Participants.Single(participant => participant.Id == slot.ParticipantId).Unseated(now);
                match.Unseat(slot, now);
            }
        }

        foreach (var match in upcoming)
        {
            SeatFrom(line, match, teamSize, now);
        }

        while (line.Count > 0)
        {
            var number = session.Matches.Count == 0 ? 1 : session.Matches.Max(match => match.Number) + 1;
            var match = new OpenPlayMatch(session.Id, number, now);
            session.Matches.Add(match);
            SeatFrom(line, match, teamSize, now);
        }

        // With the line used up, an empty match not yet started can only be
        // at the end: nothing to play.
        foreach (var empty in session.Matches.Where(match => !match.HasStarted && !match.Seated.Any()).ToList())
        {
            session.Matches.Remove(empty);
        }
    }

    /// <summary>
    /// Everybody in line for a match still to come, front first: the ones
    /// waiting and the ones already seated in a match not yet started. By when
    /// they joined the line; by when they first arrived when that is the same.
    /// </summary>
    public static IReadOnlyList<OpenPlayParticipant> Line(OpenPlaySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var seatedUpcoming = session.Matches
            .Where(match => !match.HasStarted)
            .SelectMany(match => match.Seated)
            .Select(slot => slot.ParticipantId)
            .ToHashSet();

        return session.Participants
            .Where(participant => participant.IsWaiting || seatedUpcoming.Contains(participant.Id))
            .OrderBy(participant => participant.QueuedAt)
            .ThenBy(participant => participant.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Where the next to join the line goes: now, or just after the last one
    /// in it if that is later. Two check-ins in the same instant — two desks,
    /// a clock read twice — still line up one behind the other.
    /// </summary>
    private static DateTimeOffset EndOfLine(OpenPlaySession session, DateTimeOffset now)
    {
        var last = session.Participants.Count == 0
            ? (DateTimeOffset?)null
            : session.Participants.Max(participant => participant.QueuedAt);

        return last is DateTimeOffset latest && latest >= now ? latest.AddTicks(1) : now;
    }

    private static void SeatFrom(Queue<OpenPlayParticipant> line, OpenPlayMatch match, int teamSize, DateTimeOffset now)
    {
        foreach (var team in OpenPlayTeam.Both)
        {
            for (var position = 1; position <= teamSize && line.Count > 0; position++)
            {
                match.Seat(line.Dequeue(), team, position, now);
            }
        }

        match.Settle(teamSize, now);
    }

    /// <summary>
    /// Takes the player out of any seat they hold. Before the match starts the
    /// seat simply goes; during it, they are marked off and the front of the
    /// line takes over the same seat.
    /// </summary>
    private static void LeaveSeat(OpenPlaySession session, OpenPlayParticipant participant, DateTimeOffset now)
    {
        foreach (var match in session.Matches.Where(match => match.Status != OpenPlayMatchStatus.Finished))
        {
            var slot = match.Seated.FirstOrDefault(candidate => candidate.ParticipantId == participant.Id);

            if (slot is null)
            {
                continue;
            }

            if (!match.IsPlaying)
            {
                match.Unseat(slot, now);
                continue;
            }

            slot.Leave(now);

            // The front of the line steps on, from a match still to come if
            // need be; the line then closes up behind them.
            var next = Line(session).FirstOrDefault(candidate => candidate.Id != participant.Id);

            if (next is not null)
            {
                foreach (var upcoming in session.Matches.Where(other => !other.HasStarted))
                {
                    foreach (var seat in upcoming.Seated.Where(seat => seat.ParticipantId == next.Id).ToList())
                    {
                        upcoming.Unseat(seat, now);
                    }
                }

                match.Seat(next, slot.Team, slot.Position, now);
            }
        }
    }
}

/// <summary>Why the desk's press of a match button was refused.</summary>
public enum LineupRefusal
{
    AlreadyStarted,
    CourtBusy,
    NotNext,
    TeamEmpty,
    Short,
    NotPlaying
}
