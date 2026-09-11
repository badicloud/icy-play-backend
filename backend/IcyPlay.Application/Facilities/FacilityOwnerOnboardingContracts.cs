namespace IcyPlay.Application.Facilities;

/// <summary>
/// Everything the admin console collects in the onboarding wizard, submitted in
/// one call. One transaction rather than a save per step, so an abandoned wizard
/// leaves nothing half-built in the database; the browser holds the draft until
/// the admin is ready.
/// </summary>
public sealed record OnboardFacilityOwnerRequest(
    OwnerAccountInput Owner,
    BusinessInput Business,
    IReadOnlyCollection<OwnerDocumentInput> Documents,
    FacilityInput Facility,
    IReadOnlyCollection<OperatingHourInput> OperatingHours,
    ContractInput Contract);

public sealed record OwnerAccountInput(
    string FullName,
    string Email,
    string? PhoneNumber);

public sealed record BusinessInput(
    string BusinessName,
    string BillingEmail,
    string? BillingPhone,
    string? BusinessRegistrationNumber);

public sealed record OwnerDocumentInput(
    string DocumentType,
    string PublicId,
    string SecureUrl,
    string FileName,
    string ContentType,
    long SizeInBytes);

public sealed record FacilityInput(
    string Name,
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
    IReadOnlyCollection<Guid> AmenityIds);

public sealed record OperatingHourInput(
    DayOfWeek DayOfWeek,
    TimeOnly? OpensAt,
    TimeOnly? ClosesAt);

public sealed record ContractInput(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Notes,
    /// <summary>The signed agreement. Required on every new term.</summary>
    UploadedFileInput Document);

/// <summary>
/// A file already in Cloudinary, described by what the browser posted back.
/// The URL is checked against the configured cloud before it is stored.
/// </summary>
public sealed record UploadedFileInput(
    string PublicId,
    string SecureUrl,
    string FileName,
    string ContentType,
    long SizeInBytes);

public sealed record OnboardedFacilityOwnerResponse(
    Guid UserId,
    Guid FacilityOwnerId,
    Guid FacilityId,
    string FacilitySlug,
    string Status,
    bool InvitationEmailSent);

/// <summary>One row of the admin console's facility owner list.</summary>
public sealed record FacilityOwnerListItem(
    Guid Id,
    Guid UserId,
    string BusinessName,
    string OwnerName,
    string Email,
    string? BillingPhone,
    string Status,
    int FacilityCount,
    DateOnly? ContractStartDate,
    DateOnly? ContractEndDate,
    DateTimeOffset CreatedAt);

public sealed record AmenityListItem(
    Guid Id,
    string Key,
    string Name,
    string Category,
    int DisplayOrder);

public enum OnboardingFailure
{
    None,
    DuplicateEmail,
    DuplicateSlug,
    UnknownAmenity,
    UntrustedAssetUrl,
    UntrustedContractDocument,
    InvalidContractDates,
    InvalidOperatingHours,
    UnknownTimeZone
}

public sealed record OnboardingResult<T>(T? Value, OnboardingFailure Failure = OnboardingFailure.None)
{
    public bool Succeeded => Failure == OnboardingFailure.None;
    public static OnboardingResult<T> Success(T value) => new(value);
    public static OnboardingResult<T> Fail(OnboardingFailure failure) => new(default, failure);
}
