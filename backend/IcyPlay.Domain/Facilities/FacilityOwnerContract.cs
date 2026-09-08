using IcyPlay.Domain.Common;
using IcyPlay.Domain.Identity;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One commencement period for a facility owner. Its own record rather than two
/// columns on the owner, because contracts renew: last year's term and this
/// year's have to coexist, and platform fee pricing will hang off a specific
/// term rather than off whatever dates happen to be current.
/// </summary>
public sealed class FacilityOwnerContract : Entity
{
    private FacilityOwnerContract()
    {
    }

    public FacilityOwnerContract(
        Guid facilityOwnerId,
        DateOnly startDate,
        DateOnly endDate,
        Guid commencedByUserId,
        string? notes,
        DateTimeOffset commencedAt)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("A contract cannot end before it starts.", nameof(endDate));
        }

        FacilityOwnerId = facilityOwnerId;
        StartDate = startDate;
        EndDate = endDate;
        CommencedByUserId = commencedByUserId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        CreatedAt = commencedAt;
    }

    public Guid FacilityOwnerId
    {
        get; private set;
    }
    public FacilityOwner FacilityOwner { get; private set; } = null!;
    public DateOnly StartDate
    {
        get; private set;
    }
    public DateOnly EndDate
    {
        get; private set;
    }
    /// <summary>The admin who commenced this term.</summary>
    public Guid CommencedByUserId
    {
        get; private set;
    }
    public string? Notes
    {
        get; private set;
    }
    public DateTimeOffset? CancelledAt
    {
        get; private set;
    }

    public bool Covers(DateOnly date) =>
        CancelledAt is null && StartDate <= date && date <= EndDate;

    public void Cancel(DateTimeOffset now)
    {
        CancelledAt = now;
        UpdatedAt = now;
    }
}

/// <summary>
/// The dates of one live contract, so status can be derived from a projection
/// without loading whole entities.
/// </summary>
public readonly record struct ContractTerm(DateOnly StartDate, DateOnly EndDate);
