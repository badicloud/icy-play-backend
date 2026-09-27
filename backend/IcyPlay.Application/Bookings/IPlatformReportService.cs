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
    /// <summary>
    /// The utilisation figures cut by date, court by court and period by
    /// period — what Sold Hours draws, and the trend beside each court on
    /// Sold Courts and Not Sold Courts.
    /// </summary>
    Task<PlatformReportResult<HoursOverTime>> HoursOverTimeAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>Moved Bookings, across the scope.</summary>
    Task<PlatformReportResult<MovesReport>> MovesAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>Declined Bookings, across the scope.</summary>
    Task<PlatformReportResult<DeclinesReport>> DeclinesAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>Takings, across the scope. Up to five years, the same as the desk's.</summary>
    Task<PlatformReportResult<TakingsReport>> TakingsAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>Missed Income, across the scope.</summary>
    Task<PlatformReportResult<MissedReport>> MissedAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        HoursQuery query,
        CancellationToken ct);

    /// <summary>Court Changes, across the scope. The query's court has to be one of its venues'.</summary>
    Task<PlatformReportResult<CourtChangesReport>> CourtChangesAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        CourtChangesQuery query,
        CancellationToken ct);

    /// <summary>Court Mix, across the scope.</summary>
    Task<PlatformReportResult<CourtMixReport>> CourtMixAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        CourtMixQuery query,
        CancellationToken ct);

    Task<PlatformReportResult<UtilizationReport>> UtilizationAsync(
        Guid? facilityOwnerId,
        Guid? facilityId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}
