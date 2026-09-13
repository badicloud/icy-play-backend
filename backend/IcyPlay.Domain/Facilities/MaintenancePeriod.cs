using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// A stretch of time something is out of service. A period rather than a
/// switch, because the point of recording it is to be able to answer "who had a
/// booking while this was closed" — and an on/off flag cannot say when.
///
/// One table serves both levels. The facility is always named; the court is
/// named only when the closure is that court's alone. So "is this court closed"
/// is one predicate over one table rather than a union of two.
/// </summary>
public sealed class MaintenancePeriod : Entity
{
    private MaintenancePeriod()
    {
    }

    public MaintenancePeriod(
        Guid facilityId,
        Guid? courtId,
        DateTimeOffset startsAt,
        DateTimeOffset? endsAt,
        string reason,
        Guid setByUserId,
        DateTimeOffset createdAt)
    {
        if (endsAt is not null && endsAt <= startsAt)
        {
            throw new ArgumentException("Maintenance cannot end before it starts.", nameof(endsAt));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Maintenance needs a reason.", nameof(reason));
        }

        FacilityId = facilityId;
        CourtId = courtId;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Reason = reason.Trim();
        SetByUserId = setByUserId;
        CreatedAt = createdAt;
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;

    /// <summary>Null when the whole facility is closed, which closes every court in it.</summary>
    public Guid? CourtId
    {
        get; private set;
    }
    public Court? Court
    {
        get; private set;
    }

    public DateTimeOffset StartsAt
    {
        get; private set;
    }

    /// <summary>Null means until further notice.</summary>
    public DateTimeOffset? EndsAt
    {
        get; private set;
    }

    /// <summary>Required, because this is what the affected customers are told.</summary>
    public string Reason { get; private set; } = string.Empty;

    public Guid SetByUserId
    {
        get; private set;
    }

    /// <summary>Set when someone ends the closure early rather than letting it run out.</summary>
    public DateTimeOffset? LiftedAt
    {
        get; private set;
    }

    public bool AppliesToWholeFacility => CourtId is null;

    public bool Covers(DateTimeOffset moment) =>
        LiftedAt is null && StartsAt <= moment && (EndsAt is null || moment < EndsAt);

    /// <summary>Ends the closure now, leaving the record of it in place.</summary>
    public void Lift(DateTimeOffset now)
    {
        LiftedAt = now;
        UpdatedAt = now;
    }
}
