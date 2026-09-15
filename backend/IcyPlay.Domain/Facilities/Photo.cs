using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// A picture of a venue or of one court in it. One table for both, the way
/// maintenance works: the facility is always named, and the court only when the
/// photo is of that court in particular.
///
/// The public id is the source of truth. It survives folder moves and lets each
/// surface ask for the size it needs, rather than shipping one oversized
/// original to a thumbnail.
/// </summary>
public sealed class Photo : Entity
{
    private Photo()
    {
    }

    public Photo(
        Guid facilityId,
        Guid? courtId,
        Guid? sportId,
        string publicId,
        string secureUrl,
        string? caption,
        int displayOrder,
        bool isCover,
        DateTimeOffset createdAt)
    {
        FacilityId = facilityId;
        CourtId = courtId;
        SportId = sportId;
        PublicId = publicId.Trim();
        SecureUrl = secureUrl.Trim();
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        DisplayOrder = displayOrder;
        IsCover = isCover;
        CreatedAt = createdAt;
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;

    /// <summary>Null when the photo is of the venue rather than of one court.</summary>
    public Guid? CourtId
    {
        get; private set;
    }
    public Court? Court
    {
        get; private set;
    }

    /// <summary>
    /// The sport this picture shows the court set up for, when it shows one in
    /// particular. Null is the ordinary case: a picture of the floor, good for
    /// whatever is played on it.
    ///
    /// A hall marked out for basketball looks nothing like the same hall marked
    /// out three ways for pickleball, and a customer browsing pickleball should
    /// see the pickleball markings. Only a court photo carries this — the
    /// entrance and the car park belong to no sport.
    ///
    /// A tag outlives the sport leaving the court. The listing only ever asks
    /// for a sport the court still offers, so a stale tag shows nobody
    /// anything, and clearing it would throw away a picture that comes back
    /// into use the moment the sport does.
    /// </summary>
    public Guid? SportId
    {
        get; private set;
    }
    public Sport? Sport
    {
        get; private set;
    }

    public string PublicId { get; private set; } = string.Empty;

    /// <summary>
    /// Cloudinary's <c>secure_url</c>, never its <c>url</c>: the latter is http
    /// and would be blocked as mixed content on an https page.
    /// </summary>
    public string SecureUrl { get; private set; } = string.Empty;

    public string? Caption
    {
        get; private set;
    }
    public int DisplayOrder
    {
        get; private set;
    }

    /// <summary>
    /// The one shown in the booking portal and the booking list. Exactly one per
    /// subject; the service is what keeps that true, because only it can see
    /// the others.
    /// </summary>
    public bool IsCover
    {
        get; private set;
    }

    public bool BelongsToWholeFacility => CourtId is null;

    public void SetCover(bool isCover, DateTimeOffset now)
    {
        IsCover = isCover;
        UpdatedAt = now;
    }

    public void Describe(string? caption, int displayOrder, DateTimeOffset now)
    {
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        DisplayOrder = displayOrder;
        UpdatedAt = now;
    }

    public void ShowsSport(Guid? sportId, DateTimeOffset now)
    {
        SportId = sportId;
        UpdatedAt = now;
    }
}
