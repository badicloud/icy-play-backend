using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// A photo the desk takes once a session is over — its best players, its
/// winning team or everybody who came — for the players to download and
/// share. Kept for <see cref="Retention"/>, then deleted from storage and here.
/// </summary>
public sealed class OpenPlaySessionPhoto : Entity
{
    /// <summary>How long a photo is kept, as the venue agreed: two days.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(2);

    private OpenPlaySessionPhoto()
    {
    }

    public OpenPlaySessionPhoto(
        Guid sessionId,
        string kind,
        string publicId,
        string secureUrl,
        Guid takenByUserId,
        DateTimeOffset takenAt)
    {
        if (!OpenPlayResultPhotoKind.All.Contains(kind))
        {
            throw new ArgumentException($"'{kind}' is not a kind of result photo.", nameof(kind));
        }

        SessionId = sessionId;
        Kind = kind;
        PublicId = publicId;
        SecureUrl = secureUrl;
        TakenByUserId = takenByUserId;
        CreatedAt = takenAt;
        ExpiresAt = takenAt + Retention;
    }

    public Guid SessionId
    {
        get; private set;
    }

    /// <summary>One of <see cref="OpenPlayResultPhotoKind"/>.</summary>
    public string Kind { get; private set; } = OpenPlayResultPhotoKind.BestPlayers;

    /// <summary>Cloudinary's id for it: what deleting it takes.</summary>
    public string PublicId { get; private set; } = string.Empty;

    public string SecureUrl { get; private set; } = string.Empty;

    public Guid TakenByUserId
    {
        get; private set;
    }

    /// <summary>From then on it is deleted: two days after it was taken, or at once when retired.</summary>
    public DateTimeOffset ExpiresAt
    {
        get; private set;
    }

    /// <summary>When a newer photo took its place, or the desk took it away.</summary>
    public DateTimeOffset? RetiredAt
    {
        get; private set;
    }

    /// <summary>Taken away or replaced: no longer shown, and deleted at the next clean-up.</summary>
    public void Retire(DateTimeOffset now)
    {
        RetiredAt = now;
        ExpiresAt = now;
        UpdatedAt = now;
    }
}

/// <summary>What a session's result photo is of.</summary>
public static class OpenPlayResultPhotoKind
{
    /// <summary>Everyone level on the most wins.</summary>
    public const string BestPlayers = "BestPlayers";

    /// <summary>The winners of the last match won.</summary>
    public const string WinningTeam = "WinningTeam";

    /// <summary>Everybody who came: the teams change every game, so the group as a whole.</summary>
    public const string Group = "Group";

    public static readonly IReadOnlyCollection<string> All = [BestPlayers, WinningTeam, Group];
}
