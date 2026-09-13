using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One sport a court accommodates. Its own record rather than a tag, because
/// pricing will hang off the pair: a court that takes badminton and basketball
/// will carry a price for each.
/// </summary>
public sealed class CourtSport : Entity
{
    private CourtSport()
    {
    }

    public CourtSport(Guid courtId, Guid sportId, bool isPrimary, DateTimeOffset createdAt)
    {
        CourtId = courtId;
        SportId = sportId;
        IsPrimary = isPrimary;
        CreatedAt = createdAt;
    }

    public Guid CourtId
    {
        get; private set;
    }
    public Court Court { get; private set; } = null!;
    public Guid SportId
    {
        get; private set;
    }
    public Sport Sport { get; private set; } = null!;

    /// <summary>What the court is listed as when one name has to be shown.</summary>
    public bool IsPrimary
    {
        get; private set;
    }

    public void SetPrimary(bool isPrimary, DateTimeOffset now)
    {
        IsPrimary = isPrimary;
        UpdatedAt = now;
    }
}
