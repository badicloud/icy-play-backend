namespace IcyPlay.Domain.Facilities;

/// <summary>
/// Derived from the owner's contracts rather than stored, so the status can
/// never disagree with the dates it is supposed to describe.
/// </summary>
public enum FacilityOwnerStatus
{
    /// <summary>Encoded by an admin, but no contract has commenced yet.</summary>
    Pending = 0,
    /// <summary>A contract covers today. The facility is bookable.</summary>
    Commenced = 1,
    /// <summary>Every contract has ended. Not bookable until renewed.</summary>
    Expired = 2,
    /// <summary>Switched off by an admin, whatever the contract says.</summary>
    Suspended = 3
}
