using FluentValidation;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// Editing is split by section rather than offered as one save over the whole
/// owner. A single button across forty fields makes every correction a risk,
/// and it leaves an audit entry that says only "everything changed" — which is
/// the same as saying nothing.
/// </summary>
public sealed record UpdateBusinessRequest(
    string BusinessName,
    string BillingEmail,
    string? BillingPhone,
    string? BusinessRegistrationNumber,
    string? Reason);

public sealed record UpdateFacilityRequest(
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
    IReadOnlyCollection<Guid> AmenityIds,
    string? Reason);

public sealed record UpdateOperatingHoursRequest(
    IReadOnlyCollection<OperatingHourInput> OperatingHours,
    string? Reason);

public sealed record RenewContractRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Notes,
    UploadedFileInput Document,
    string? Reason);

/// <summary>Replaces the agreement on a term that already exists.</summary>
public sealed record ReplaceContractDocumentRequest(
    UploadedFileInput Document,
    string? Reason);

public sealed record CancelContractRequest(string? Reason);

public enum EditFailure
{
    None,
    NotFound,
    UnknownAmenity,
    UnknownTimeZone,
    DuplicateSlug,
    AlreadyCancelled,
    OverlappingContract,
    UntrustedContractDocument
}

public sealed record EditResult(EditFailure Failure = EditFailure.None)
{
    public bool Succeeded => Failure == EditFailure.None;
    public static EditResult Success() => new();
    public static EditResult Fail(EditFailure failure) => new(failure);
}

public sealed class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BillingEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.BillingPhone).MaximumLength(50);
        RuleFor(x => x.BusinessRegistrationNumber).MaximumLength(100);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class UpdateFacilityRequestValidator : AbstractValidator<UpdateFacilityRequest>
{
    public UpdateFacilityRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AddressLine1).NotEmpty().MaximumLength(300);
        RuleFor(x => x.AddressLine2).MaximumLength(300);
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Province).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PostalCode).MaximumLength(20);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ContactPhone).MaximumLength(50);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x.Longitude).NotNull().When(x => x.Latitude is not null)
            .WithMessage("A latitude needs a longitude.");
        RuleFor(x => x.Latitude).NotNull().When(x => x.Longitude is not null)
            .WithMessage("A longitude needs a latitude.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class UpdateOperatingHoursRequestValidator : AbstractValidator<UpdateOperatingHoursRequest>
{
    public UpdateOperatingHoursRequestValidator()
    {
        RuleForEach(x => x.OperatingHours).SetValidator(new OperatingHourInputValidator());
        RuleFor(x => x.OperatingHours)
            .Must(hours => hours.Select(hour => hour.DayOfWeek).Distinct().Count() == hours.Count)
            .WithMessage("Each day of the week can appear only once.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class ReplaceContractDocumentRequestValidator
    : AbstractValidator<ReplaceContractDocumentRequest>
{
    public ReplaceContractDocumentRequestValidator()
    {
        RuleFor(x => x.Document).NotNull().SetValidator(new UploadedFileInputValidator()!);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class RenewContractRequestValidator : AbstractValidator<RenewContractRequest>
{
    public RenewContractRequestValidator()
    {
        RuleFor(x => x.Document)
            .NotNull()
            .WithMessage("Attach the signed agreement.")
            .SetValidator(new UploadedFileInputValidator()!);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("A contract cannot end before it starts.");
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CancelContractRequestValidator : AbstractValidator<CancelContractRequest>
{
    public CancelContractRequestValidator() => RuleFor(x => x.Reason).MaximumLength(500);
}
