using IcyPlay.Domain.Common;
using IcyPlay.Domain.Identity;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// Somebody who works one venue: checks the GCash receipts that come in and
/// confirms the bookings behind them.
///
/// Scoped to a facility rather than to the owner. An owner with three venues
/// across the city has three sets of staff, and the person on the desk at one
/// has no business confirming payments at another.
///
/// The owner is deliberately NOT a row here. See <see cref="Facility.IsAttendedBy"/>.
/// </summary>
public sealed class FacilityAttendant : Entity
{
    private FacilityAttendant()
    {
    }

    public FacilityAttendant(Guid facilityId, Guid userId, DateTimeOffset createdAt)
    {
        FacilityId = facilityId;
        UserId = userId;
        CreatedAt = createdAt;
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;

    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;

    /// <summary>
    /// False once they are taken off the venue. Retired rather than deleted, so
    /// a booking they confirmed still has somebody's name against it.
    ///
    /// One way only: an address that has an account cannot be added to a desk,
    /// and a retired attendant's address has one. Somebody taken off by mistake
    /// is a job for a gesture that says so, not for adding them again.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public void Retire(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = now;
    }
}
