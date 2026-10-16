using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// One checked-in player's place in a session's line for matches: a registered
/// player or a free one, never both. Made when they check in; the line is
/// ordered by <see cref="QueuedAt"/>, which is when they arrived — and, after a
/// match, when they went back to the end of it.
/// </summary>
public sealed class OpenPlayParticipant : Entity
{
    private OpenPlayParticipant()
    {
    }

    public OpenPlayParticipant(Guid sessionId, Guid? registrationId, Guid? freePlayerId, DateTimeOffset queuedAt)
    {
        if ((registrationId is null) == (freePlayerId is null))
        {
            throw new ArgumentException("A participant is a registered player or a free one, never both or neither.");
        }

        SessionId = sessionId;
        RegistrationId = registrationId;
        FreePlayerId = freePlayerId;
        QueuedAt = queuedAt;
        Status = OpenPlayParticipantStatus.Waiting;
        CreatedAt = queuedAt;
    }

    public Guid SessionId
    {
        get; private set;
    }

    public Guid? RegistrationId
    {
        get; private set;
    }

    public Guid? FreePlayerId
    {
        get; private set;
    }

    /// <summary>Their place in the line: earlier plays first.</summary>
    public DateTimeOffset QueuedAt
    {
        get; private set;
    }

    /// <summary>One of <see cref="OpenPlayParticipantStatus"/>.</summary>
    public string Status { get; private set; } = OpenPlayParticipantStatus.Waiting;

    /// <summary>When the desk said they were done for the day.</summary>
    public DateTimeOffset? DoneAt
    {
        get; private set;
    }

    /// <summary>
    /// The team the desk moved them to in the next match, to even the sides
    /// out; null for the team their place in line gives them. Forgotten once
    /// they have played, are done, or go to the end of the line.
    /// </summary>
    public string? PinnedTeam
    {
        get; private set;
    }

    public bool IsWaiting => Status == OpenPlayParticipantStatus.Waiting;

    public bool IsDone => Status == OpenPlayParticipantStatus.Done;

    internal void Seated(DateTimeOffset now)
    {
        Status = OpenPlayParticipantStatus.InMatch;
        UpdatedAt = now;
    }

    /// <summary>Off a match not yet started, still in line at the same place.</summary>
    internal void Unseated(DateTimeOffset now)
    {
        Status = OpenPlayParticipantStatus.Waiting;
        UpdatedAt = now;
    }

    /// <summary>Back to the end of the line: after a match, or back in after being done.</summary>
    internal void BackToTheLine(DateTimeOffset queuedAt, DateTimeOffset now)
    {
        Status = OpenPlayParticipantStatus.Waiting;
        QueuedAt = queuedAt;
        DoneAt = null;
        PinnedTeam = null;
        UpdatedAt = now;
    }

    /// <summary>Onto a team in their next match, or back to the one their place gives them with null.</summary>
    internal void PinTo(string? team, DateTimeOffset now)
    {
        PinnedTeam = team;
        UpdatedAt = now;
    }

    /// <summary>To another place in the line, staying as they are otherwise.</summary>
    internal void MoveInLine(DateTimeOffset queuedAt, DateTimeOffset now)
    {
        QueuedAt = queuedAt;
        UpdatedAt = now;
    }

    internal void MarkDone(DateTimeOffset now)
    {
        Status = OpenPlayParticipantStatus.Done;
        DoneAt = now;
        PinnedTeam = null;
        UpdatedAt = now;
    }
}

public static class OpenPlayParticipantStatus
{
    /// <summary>In the line, not in a match yet.</summary>
    public const string Waiting = "Waiting";

    /// <summary>On a team in a match that is forming, ready or playing.</summary>
    public const string InMatch = "InMatch";

    /// <summary>Out for the day: tired, hurt or gone. The desk can put them back in.</summary>
    public const string Done = "Done";
}
