using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One day of the week for a court that keeps its own hours. Only written when
/// the court has opted out of the facility's, so an empty set means the court
/// follows the building rather than that nobody has filled it in.
/// </summary>
public sealed class CourtOperatingHour : Entity
{
    private CourtOperatingHour()
    {
    }

    public CourtOperatingHour(
        Guid courtId,
        DayOfWeek dayOfWeek,
        TimeOnly? opensAt,
        TimeOnly? closesAt,
        DateTimeOffset createdAt)
    {
        Validate(opensAt, closesAt);

        CourtId = courtId;
        DayOfWeek = dayOfWeek;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        CreatedAt = createdAt;
    }

    public Guid CourtId
    {
        get; private set;
    }
    public Court Court { get; private set; } = null!;
    public DayOfWeek DayOfWeek
    {
        get; private set;
    }

    /// <summary>Wall-clock time in the facility's zone, never UTC.</summary>
    public TimeOnly? OpensAt
    {
        get; private set;
    }
    public TimeOnly? ClosesAt
    {
        get; private set;
    }

    public bool IsClosed => OpensAt is null || ClosesAt is null;

    public void SetHours(TimeOnly? opensAt, TimeOnly? closesAt, DateTimeOffset now)
    {
        Validate(opensAt, closesAt);
        OpensAt = opensAt;
        ClosesAt = closesAt;
        UpdatedAt = now;
    }

    /// <summary>
    /// Closed is the absence of both times, not a flag beside them: a flag can
    /// disagree with the hours it sits next to, an absent pair cannot.
    /// </summary>
    private static void Validate(TimeOnly? opensAt, TimeOnly? closesAt)
    {
        if (opensAt is null != (closesAt is null))
        {
            throw new ArgumentException(
                "An opening time needs a closing time, and the other way round.",
                nameof(opensAt));
        }

        if (opensAt is not null && closesAt <= opensAt)
        {
            throw new ArgumentException(
                "A court cannot close before it opens.",
                nameof(closesAt));
        }
    }
}
