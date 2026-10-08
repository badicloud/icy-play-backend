using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Payments;

/// <summary>
/// How far one person at a desk has read a list that keeps growing — the
/// online payments coming in — so the badge counts only what arrived since
/// they last looked.
///
/// Per person, not per venue: an owner opening the list must not clear the
/// badge on an attendant's screen, who has not seen any of it.
/// </summary>
public sealed class DeskReadMarker : Entity
{
    private DeskReadMarker()
    {
    }

    public DeskReadMarker(Guid userId, string feed, DateTimeOffset seenAt)
    {
        UserId = userId;
        Feed = feed;
        SeenAt = seenAt;
        CreatedAt = seenAt;
    }

    public Guid UserId
    {
        get; private set;
    }

    /// <summary>Which list. See <see cref="DeskFeed"/>.</summary>
    public string Feed { get; private set; } = string.Empty;

    /// <summary>Everything that arrived up to here has been seen.</summary>
    public DateTimeOffset SeenAt
    {
        get; private set;
    }

    public void SeenUpTo(DateTimeOffset moment)
    {
        // Never backwards: two tabs open at once must not un-read anything.
        if (moment > SeenAt)
        {
            SeenAt = moment;
            UpdatedAt = moment;
        }
    }
}

public static class DeskFeed
{
    /// <summary>Payments made through the gateway at the person's venues.</summary>
    public const string OnlineTransactions = "OnlineTransactions";
}
