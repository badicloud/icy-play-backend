using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One day of the week for one facility. Seven rows per facility, so the owner
/// with eight courts types the schedule once rather than eight times; a court
/// that keeps different hours overrides it.
/// </summary>
public sealed class FacilityOperatingHour : Entity
{
    private FacilityOperatingHour()
    {
    }

    public FacilityOperatingHour(
        Guid facilityId,
        DayOfWeek dayOfWeek,
        TimeOnly? opensAt,
        TimeOnly? closesAt,
        DateTimeOffset createdAt)
    {
        Validate(opensAt, closesAt);

        FacilityId = facilityId;
        DayOfWeek = dayOfWeek;
        OpensAt = opensAt;
        ClosesAt = closesAt;
        CreatedAt = createdAt;
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;
    public DayOfWeek DayOfWeek
    {
        get; private set;
    }

    /// <summary>Wall-clock time in the facility's own zone, never UTC.</summary>
    public TimeOnly? OpensAt
    {
        get; private set;
    }
    public TimeOnly? ClosesAt
    {
        get; private set;
    }

    public bool IsClosed => OpensAt is null || ClosesAt is null;

    /// <summary>
    /// Rewrites one day in place. Updating beats deleting the week and
    /// inserting it again: the row keeps its identity, and a delete and an
    /// insert on the same (facility, day) pair inside one save collide on the
    /// unique index that stops a facility holding two answers for Monday.
    /// </summary>
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
                "A facility cannot close before it opens.",
                nameof(closesAt));
        }
    }
}
