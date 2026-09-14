namespace IcyPlay.Domain.Identity;

public static class UserRoleName
{
    public const string Customer = "Customer";
    public const string FacilityOwner = "FacilityOwner";

    /// <summary>
    /// Works a venue's desk: checks payment receipts and confirms the bookings
    /// behind them. Which venues, and whether they still work there, is on
    /// <see cref="Facilities.FacilityAttendant"/> rather than on the role — a
    /// role says what someone may do, not where.
    /// </summary>
    public const string FacilityAttendant = "FacilityAttendant";
    public const string PlatformAdmin = "PlatformAdmin";
}
