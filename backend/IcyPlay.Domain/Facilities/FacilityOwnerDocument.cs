using IcyPlay.Domain.Common;
using IcyPlay.Domain.Identity;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// Metadata for one verification document. The file itself lives in Cloudinary;
/// SQL Server holds only the reference, per the upload flow in
/// <c>docs/api-design.md</c>.
/// </summary>
public sealed class FacilityOwnerDocument : Entity
{
    private FacilityOwnerDocument()
    {
    }

    public FacilityOwnerDocument(
        Guid facilityOwnerId,
        string documentType,
        string publicId,
        string secureUrl,
        string fileName,
        string contentType,
        long sizeInBytes,
        DateTimeOffset uploadedAt)
    {
        FacilityOwnerId = facilityOwnerId;
        DocumentType = documentType;
        PublicId = publicId.Trim();
        SecureUrl = secureUrl.Trim();
        FileName = fileName.Trim();
        ContentType = contentType.Trim();
        SizeInBytes = sizeInBytes;
        CreatedAt = uploadedAt;
    }

    public Guid FacilityOwnerId
    {
        get; private set;
    }
    public FacilityOwner FacilityOwner { get; private set; } = null!;
    public string DocumentType { get; private set; } = string.Empty;

    /// <summary>
    /// The source of truth for the asset. Survives folder moves and lets each
    /// surface build the size of URL it actually needs.
    /// </summary>
    public string PublicId { get; private set; } = string.Empty;

    /// <summary>
    /// Cloudinary's <c>secure_url</c>, never its <c>url</c>: the latter is http
    /// and would be blocked as mixed content on an https page.
    /// </summary>
    public string SecureUrl { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeInBytes
    {
        get; private set;
    }
}
