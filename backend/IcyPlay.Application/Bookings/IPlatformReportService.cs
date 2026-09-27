namespace IcyPlay.Application.Bookings;

/// <summary>
/// The venue desk's reports, for the platform admin: every venue on the
/// platform, or one facility owner's, or one venue.
///
/// The same arithmetic the desk runs — each report is worked out over a list of
/// venues, and only who decides that list differs. A desk's list is the venues
/// its person works; the admin's is whatever it filters to.
/// </summary>
public interface IPlatformReportService
{
    /// <summary>Every facility owner and their venues, for the report filters.</summary>
    Task<IReadOnlyCollection<ReportOwner>> OwnersAsync(CancellationToken ct);

    /// <summary>
    /// The platform this minute: the desk's five numbers across every venue in
    /// scope, and owner by owner.
    /// </summary>
    Task<PlatformReportResult<PlatformSnapshot>> SnapshotAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        CancellationToken ct);

    /// <summary>
    /// Court utilisation across the venues in scope — the desk's report, with
    /// the money in it: the admin sees what every owner sees of their own.
    /// </summary>
    Task<PlatformReportResult<UtilizationReport>> UtilizationAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
