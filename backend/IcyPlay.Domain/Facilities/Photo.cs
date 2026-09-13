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
        string publicId,
        string secureUrl,
        string? caption,
        int displayOrder,
        bool isCover,
        DateTimeOffset createdAt)
    {
        FacilityId = facilityId;
        CourtId = courtId;
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
}
