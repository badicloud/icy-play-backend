using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// One game in a session: two teams, filled from the line in order — Team A
/// first, then Team B. It forms as players check in, is ready once both teams
/// are full, is started and finished by the desk.
/// </summary>
public sealed class OpenPlayMatch : Entity
{
    private OpenPlayMatch()
    {
    }

    public OpenPlayMatch(Guid sessionId, int number, DateTimeOffset createdAt)
    {
        SessionId = sessionId;
        Number = number;
        Status = OpenPlayMatchStatus.Forming;
        CreatedAt = createdAt;
    }

    public Guid SessionId
    {
        get; private set;
    }

    /// <summary>Match 1, Match 2: the order they are played in.</summary>
    public int Number
    {
        get; private set;
    }

    /// <summary>One of <see cref="OpenPlayMatchStatus"/>.</summary>
    public string Status { get; private set; } = OpenPlayMatchStatus.Forming;

    public DateTimeOffset? StartedAt
    {
        get; private set;
    }

    public Guid? StartedByUserId
    {
        get; private set;
    }

    public DateTimeOffset? FinishedAt
    {
        get; private set;
    }

    public Guid? FinishedByUserId
    {
        get; private set;
    }

    /// <summary>Who won: one of <see cref="OpenPlayMatchResult"/>. Set when it finishes.</summary>
    public string? Result
    {
        get; private set;
    }

    /// <summary>The score, when the desk gave one. Both or neither.</summary>
    public int? ScoreA
    {
        get; private set;
    }

    public int? ScoreB
    {
        get; private set;
    }

    /// <summary>
    /// Every seat ever taken, including those left mid-game — so a finished
    /// match still says who played. The ones still on court have no
    /// <see cref="OpenPlayMatchSlot.LeftAt"/>.
    /// </summary>
    public ICollection<OpenPlayMatchSlot> Slots { get; private set; } = [];

    public bool HasStarted => Status is OpenPlayMatchStatus.Playing or OpenPlayMatchStatus.Finished;

    public bool IsPlaying => Status == OpenPlayMatchStatus.Playing;

    public IEnumerable<OpenPlayMatchSlot> Seated => Slots.Where(slot => slot.LeftAt is null);

    public int SeatedOn(string team) => Seated.Count(slot => slot.Team == team);

    public bool IsFull(int teamSize) =>
        SeatedOn(OpenPlayTeam.A) >= teamSize && SeatedOn(OpenPlayTeam.B) >= teamSize;

    internal OpenPlayMatchSlot Seat(OpenPlayParticipant participant, string team, int position, DateTimeOffset now)
    {
        var slot = new OpenPlayMatchSlot(Id, participant.Id, team, position, now);
        Slots.Add(slot);
        participant.Seated(now);
        UpdatedAt = now;

        return slot;
    }

    /// <summary>Takes a seat back before the match starts: it was never played.</summary>
    internal void Unseat(OpenPlayMatchSlot slot, DateTimeOffset now)
    {
        Slots.Remove(slot);
        UpdatedAt = now;
    }

    /// <summary>Ready once both teams are full; forming until then. Not for a match under way.</summary>
    internal void Settle(int teamSize, DateTimeOffset now)
    {
        if (HasStarted)
        {
            return;
        }

        var status = IsFull(teamSize) ? OpenPlayMatchStatus.Ready : OpenPlayMatchStatus.Forming;

        if (status != Status)
        {
            Status = status;
            UpdatedAt = now;
        }
    }

    internal void Start(Guid startedByUserId, DateTimeOffset now)
    {
        Status = OpenPlayMatchStatus.Playing;
        StartedAt = now;
        StartedByUserId = startedByUserId;
        UpdatedAt = now;
    }

    internal void Finish(Guid finishedByUserId, string result, int? scoreA, int? scoreB, DateTimeOffset now)
    {
        Status = OpenPlayMatchStatus.Finished;
        FinishedAt = now;
        FinishedByUserId = finishedByUserId;
        Record(result, scoreA, scoreB, now);
    }

    /// <summary>Puts a mistaken result right, after the match.</summary>
    internal void Record(string result, int? scoreA, int? scoreB, DateTimeOffset now)
    {
        Result = result;
        ScoreA = scoreA;
        ScoreB = scoreB;
        UpdatedAt = now;
    }
}

/// <summary>One player's seat on one team of a match.</summary>
public sealed class OpenPlayMatchSlot : Entity
{
    private OpenPlayMatchSlot()
    {
    }

    public OpenPlayMatchSlot(Guid matchId, Guid participantId, string team, int position, DateTimeOffset seatedAt)
    {
        MatchId = matchId;
        ParticipantId = participantId;
        Team = team;
        Position = position;
        CreatedAt = seatedAt;
    }

    public Guid MatchId
    {
        get; private set;
    }
    public OpenPlayMatch Match { get; private set; } = null!;

    public Guid ParticipantId
    {
        get; private set;
    }

    /// <summary><see cref="OpenPlayTeam.A"/> or <see cref="OpenPlayTeam.B"/>.</summary>
    public string Team { get; private set; } = OpenPlayTeam.A;

    /// <summary>1 to the team's size: the order seats were filled in.</summary>
    public int Position
    {
        get; private set;
    }

    /// <summary>When they came off mid-game, done for the day, and somebody may have taken over.</summary>
    public DateTimeOffset? LeftAt
    {
        get; private set;
    }

    internal void Leave(DateTimeOffset now)
    {
        LeftAt = now;
        UpdatedAt = now;
    }
}

public static class OpenPlayMatchStatus
{
    /// <summary>Still filling from the line.</summary>
    public const string Forming = "Forming";

    /// <summary>Both teams full, waiting for the court.</summary>
    public const string Ready = "Ready";

    public const string Playing = "Playing";
    public const string Finished = "Finished";
}

/// <summary>How a match ended.</summary>
public static class OpenPlayMatchResult
{
    public const string TeamA = "A";
    public const string TeamB = "B";

    /// <summary>Level when it ended — time ran out on a tie, as often as not.</summary>
    public const string Draw = "Draw";

    public const int HighestScore = 999;

    public static readonly IReadOnlyCollection<string> All = [TeamA, TeamB, Draw];

    /// <summary>
    /// Whether a result and its score hold together: a known result, a score
    /// given for both teams or neither, and a score that says the same as the
    /// result — the winner ahead, a draw level.
    /// </summary>
    public static bool Holds(string? result, int? scoreA, int? scoreB)
    {
        if (result is null || !All.Contains(result))
        {
            return false;
        }

        if (scoreA is null && scoreB is null)
        {
            return true;
        }

        if (scoreA is not int a || scoreB is not int b || a < 0 || b < 0 || a > HighestScore || b > HighestScore)
        {
            return false;
        }

        return result switch
        {
            TeamA => a > b,
            TeamB => b > a,
            _ => a == b
        };
    }
}

public static class OpenPlayTeam
{
    public const string A = "A";
    public const string B = "B";

    public static readonly IReadOnlyList<string> Both = [A, B];
}
