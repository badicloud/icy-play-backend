namespace IcyPlay.Application.Facilities;

/// <summary>
/// One thing customers can actually book somewhere: a sport played on a court,
/// or an event the floor is hired for. Only activities with a court configured
/// appear, because a filter that returns nothing is worse than a filter that
/// was never offered.
/// </summary>
public sealed record CatalogActivity(
    Guid Id,
    string Key,
    string Name,
    string Category,
    /// <summary>"Sport" or "Event".</summary>
    string Kind,
    /// <summary>How many courts are set up for it, counting each division separately.</summary>
    int CourtCount,
    int FacilityCount);

/// <summary>
/// One thing a customer can book: a court, or one marked-out part of a court.
/// A floor divided three ways for pickleball is three of these, because three
/// separate games can run on it at once and each is booked on its own.
/// </summary>
public sealed record CatalogCourt(
    /// <summary>
    /// What a booking is taken against: this court, for this sport, this part
    /// of the floor. Stable across a rename and across the floor being marked
    /// out differently, which is why a booking points here and not at a court
    /// plus a number worked out on the way past.
    /// </summary>
    Guid BookableCourtId,
    Guid CourtId,
    /// <summary>Which sport or event this offering is for. A court set up for three appears three times.</summary>
    string SportKey,
    string SportName,
    /// <summary>1 when the court is played whole, otherwise which part this is.</summary>
    int DivisionNumber,
    /// <summary>What a customer sees. Derived from the court, the sport and the number.</summary>
    string Name,
    Guid FacilityId,
    string FacilityName,
    string AddressLine1,
    string City,
    string Province,
    string? PostalCode,
    /// <summary>Null until the venue has pinned itself. The map link needs both.</summary>
    decimal? Latitude,
    decimal? Longitude,
    string? CoverPhotoUrl,
    string VenueType,
    string? Surface,
    bool HasLighting,
    int SlotLengthMinutes,
    int MinimumDurationMinutes,
    /// <summary>Null when the venue has not priced this sport yet.</summary>
    decimal? StandardHourlyRate,
    decimal? PeakHourlyRate,
    decimal? WeekendRate,
    decimal? HolidayRate,
    /// <summary>
    /// When the peak rate applies. Sent with the rate, because "700 at peak" is
    /// not something a customer can act on without knowing when peak is.
    /// </summary>
    TimeOnly? PeakStartsAt,
    TimeOnly? PeakEndsAt,
    bool PeakOnWeekdays,
    bool PeakOnWeekends,
    /// <summary>True while the court, or its whole facility, is closed.</summary>
    bool IsUnderMaintenance,
    /// <summary>
    /// When the closure ends, so the page can say when to come back. Null means
    /// the venue has not said.
    ///
    /// The admin's reason is deliberately not carried: it is written for the
    /// audit trail, and "owner has not paid" is a real thing to write there and
    /// the wrong thing to show a customer.
    /// </summary>
    DateTimeOffset? MaintenanceEndsAt,
    /// <summary>
    /// True when the whole venue is closed rather than this court alone. A
    /// customer looking at one court should know the others are shut too.
    /// </summary>
    bool WholeVenueClosed);

/// <summary>
/// Everything a customer needs to decide on one bookable court: the court
/// itself, the venue around it, and what it costs. One read, because a page
/// assembled from five calls shows five different moments.
/// </summary>
public sealed record CatalogCourtDetail(
    CatalogCourt Court,
    string? CourtDescription,
    string? SizeLabel,
    int? Capacity,
    string? Equipment,
    int BufferMinutes,
    /// <summary>The court's own gallery, cover first.</summary>
    IReadOnlyCollection<PhotoItem> CourtPhotos,
    CatalogVenue Venue);

/// <summary>The venue around the court: what a customer walks into.</summary>
public sealed record CatalogVenue(
    string? Description,
    string? SafetyMeasures,
    string? HouseRules,
    string TimeZone,
    string? ContactPhone,
    string? ContactEmail,
    IReadOnlyCollection<string> Amenities,
    /// <summary>Already resolved: the facility's hours, or the court's own.</summary>
    IReadOnlyCollection<FacilityOperatingHourDetail> OperatingHours,
    IReadOnlyCollection<PhotoItem> Photos);

/// <summary>
/// What the public site can offer. Read on every landing page, changed only
/// when an admin touches a court or the lookup, so it is cached and cleared
/// rather than counted afresh for each visitor.
/// </summary>
public interface IActivityCatalog
{
    Task<IReadOnlyCollection<CatalogActivity>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every bookable court, with divided courts listed part by part. Pass a
    /// sport key to narrow it, or nothing at all for everything on offer — a
    /// visitor should see what is available before being asked to choose.
    /// </summary>
    Task<IReadOnlyCollection<CatalogCourt>> ListCourtsAsync(
        string? sportKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// One bookable court, whole. Null when the court is not on offer for that
    /// sport, or the division does not exist.
    /// </summary>
    Task<CatalogCourtDetail?> GetCourtAsync(
        Guid courtId,
        string sportKey,
        int divisionNumber,
        CancellationToken cancellationToken);

    /// <summary>
    /// Drops the cached listing. Called when a court or the lookup changes,
    /// because the alternative is a visitor filtering for a sport that was
    /// retired ten minutes ago.
    /// </summary>
    void Invalidate();
}
