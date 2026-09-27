namespace IcyPlay.Application.Bookings;

/// <summary>
/// One facility owner, as the admin's report filter lists them: their business
/// and the venues it runs, so picking an owner can narrow the venue list too.
/// </summary>
public sealed record ReportOwner(Guid Id, string BusinessName, IReadOnlyCollection<DeskVenue> Venues);

/// <summary>
/// The platform as it stands this minute: the same five numbers a venue's desk
/// shows, added up across every venue in scope, and then owner by owner.
/// </summary>
public sealed record PlatformSnapshot(
    /// <summary>Owners in scope, with or without a venue.</summary>
    int Owners,
    int Venues,
    VenueSnapshot Total,
    IReadOnlyCollection<OwnerSnapshot> PerOwner);

public sealed record OwnerSnapshot(
    Guid FacilityOwnerId,
    string BusinessName,
    int Venues,
    VenueSnapshot Snapshot);

public enum PlatformReportFailure
{
    None,
    /// <summary>No facility owner of that id.</summary>
    OwnerNotFound,
    /// <summary>No venue of that id — or none belonging to the owner asked for.</summary>
    VenueNotFound,
    /// <summary>A range that ends before it starts.</summary>
    WindowBackwards,
    /// <summary>More days than the report answers for.</summary>
    WindowTooWide,
    /// <summary>Not a grain anybody can ask a report for.</summary>
    UnknownGrain,
    /// <summary>More than the takings report answers for: five years.</summary>
    TakingsWindowTooWide,
    /// <summary>No court of that id at the venues in scope.</summary>
    CourtNotFound
}

public sealed record PlatformReportResult<T>(T? Value, PlatformReportFailure Failure = PlatformReportFailure.None)
{
    public bool Succeeded => Failure == PlatformReportFailure.None;

    public static PlatformReportResult<T> Success(T value) => new(value);

    public static PlatformReportResult<T> Fail(PlatformReportFailure failure) => new(default, failure);
}
