namespace IcyPlay.Application.Facilities;

/// <summary>
/// Everything the admin console shows on one facility owner. Deliberately a
/// single read: the console needs the business, the documents, the facility and
/// the contracts on the same screen, and six round trips to draw one page is
/// six chances for it to render half-finished.
/// </summary>
public sealed record FacilityOwnerDetail(
    Guid Id,
    Guid UserId,
    string BusinessName,
    string BillingEmail,
    string? BillingPhone,
    string? BusinessRegistrationNumber,
    bool IsActive,
    /// <summary>Derived from the contract dates, never stored.</summary>
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    OwnerAccountDetail Owner,
    IReadOnlyCollection<OwnerDocumentDetail> Documents,
    IReadOnlyCollection<FacilityDetail> Facilities,
    IReadOnlyCollection<ContractDetail> Contracts,
    InvitationStatus Invitation);

/// <summary>
/// Where the owner is between "encoded by an admin" and "signed in on their own
/// account". Derived from the tokens and the account, never stored.
/// </summary>
public sealed record InvitationStatus(
    bool IsAccepted,
    DateTimeOffset? LastSentAt,
    DateTimeOffset? ExpiresAt,
    bool HasLiveInvitation);

public sealed record OwnerAccountDetail(
    string FullName,
    string Email,
    string? PhoneNumber,
    bool IsActive,
    bool IsEmailVerified,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset CreatedAt);

public sealed record OwnerDocumentDetail(
    Guid Id,
    string DocumentType,
    string PublicId,
    /// <summary>Always https on the configured cloud; validated on the way in.</summary>
    string SecureUrl,
    string FileName,
    string ContentType,
    long SizeInBytes,
    DateTimeOffset CreatedAt);

public sealed record FacilityDetail(
    Guid Id,
    string Name,
    /// <summary>The public URL segment. Stable across a rename by design.</summary>
    string Slug,
    string? Description,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string Province,
    string? PostalCode,
    string Country,
    decimal? Latitude,
    decimal? Longitude,
    string TimeZone,
    string? ContactPhone,
    string? ContactEmail,
    string? SafetyMeasures,
    string? HouseRules,
    bool IsActive,
    IReadOnlyCollection<FacilityAmenityDetail> Amenities,
    IReadOnlyCollection<FacilityOperatingHourDetail> OperatingHours,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record FacilityAmenityDetail(Guid Id, string Key, string Name, string Category);

public sealed record FacilityOperatingHourDetail(
    int DayOfWeek,
    /// <summary>Null on both when the facility is closed that day.</summary>
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt);

public sealed record ContractDetail(
    Guid Id,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Notes,
    Guid CommencedByUserId,
    /// <summary>Who signed it off, so the term is traceable to a person.</summary>
    string? CommencedByName,
    DateTimeOffset? CancelledAt,
    /// <summary>Whether this is the term the owner is live on right now.</summary>
    bool IsLiveToday,
    /// <summary>Null on terms commenced before an agreement was required.</summary>
    ContractDocumentDetail? Document,
    DateTimeOffset CreatedAt);

public sealed record ContractDocumentDetail(
    string PublicId,
    string SecureUrl,
    string FileName,
    string ContentType,
    long SizeInBytes);
