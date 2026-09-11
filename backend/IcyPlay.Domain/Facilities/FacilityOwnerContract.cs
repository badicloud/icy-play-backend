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

    /// <summary>
    /// The signed agreement. Nullable in the database rather than required,
    /// because terms commenced before this was asked for genuinely have no
    /// document and inventing one would be worse than showing the gap. Every
    /// new term must carry one; the rule lives in validation.
    /// </summary>
    public string? DocumentPublicId
    {
        get; private set;
    }
    public string? DocumentSecureUrl
    {
        get; private set;
    }
    public string? DocumentFileName
    {
        get; private set;
    }
    public string? DocumentContentType
    {
        get; private set;
    }
    public long? DocumentSizeInBytes
    {
        get; private set;
    }

    public bool HasSignedAgreement => DocumentPublicId is not null;

    /// <summary>
    /// Attaches or replaces the signed agreement. Replacing is allowed because
    /// a wrong or unreadable scan is a real mistake, and a term that can never
    /// be corrected is worse than one that records the correction.
    /// </summary>
    public void AttachDocument(
        string publicId,
        string secureUrl,
        string fileName,
        string contentType,
        long sizeInBytes,
        DateTimeOffset now)
    {
        DocumentPublicId = publicId.Trim();
        DocumentSecureUrl = secureUrl.Trim();
        DocumentFileName = fileName.Trim();
        DocumentContentType = contentType.Trim();
        DocumentSizeInBytes = sizeInBytes;
        UpdatedAt = now;
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
