using FluentValidation;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Application.Facilities;

public sealed class OnboardFacilityOwnerRequestValidator : AbstractValidator<OnboardFacilityOwnerRequest>
{
    public OnboardFacilityOwnerRequestValidator()
    {
        RuleFor(x => x.Owner).NotNull().SetValidator(new OwnerAccountInputValidator());
        RuleFor(x => x.Business).NotNull().SetValidator(new BusinessInputValidator());
        RuleFor(x => x.Facility).NotNull().SetValidator(new FacilityInputValidator());
        RuleFor(x => x.Contract).NotNull().SetValidator(new ContractInputValidator());

        RuleForEach(x => x.Documents).SetValidator(new OwnerDocumentInputValidator());
        RuleForEach(x => x.OperatingHours).SetValidator(new OperatingHourInputValidator());

        // A facility owner encoded without proof of who they are is the exact
        // thing admin-led onboarding exists to prevent.
        RuleFor(x => x.Documents)
            .NotEmpty()
            .WithMessage("Attach at least one verification document.");

        RuleFor(x => x.OperatingHours)
            .Must(hours => hours.Select(hour => hour.DayOfWeek).Distinct().Count() == hours.Count)
            .WithMessage("Each day of the week can appear only once.");
    }
}

public sealed class OwnerAccountInputValidator : AbstractValidator<OwnerAccountInput>
{
    public OwnerAccountInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        // A mobile, not a landline: the owner is reached on this number, and
        // it is the one an SMS would go to.
        RuleFor(x => x.PhoneNumber)
            .NotEmpty()
            .MaximumLength(50)
            .Must(RegistrationValidation.IsPhilippineMobileNumber)
            .WithMessage("Enter a Philippine mobile number, like 0995 3979930.");
    }
}

public sealed class BusinessInputValidator : AbstractValidator<BusinessInput>
{
    public BusinessInputValidator()
    {
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BillingEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.BillingPhone).MaximumLength(50);
        RuleFor(x => x.BusinessRegistrationNumber).MaximumLength(100);
    }
}

public sealed class OwnerDocumentInputValidator : AbstractValidator<OwnerDocumentInput>
{
    /// <summary>Cloudinary's own free-plan ceiling, and plenty for a permit scan.</summary>
    private const long MaximumSizeInBytes = 10 * 1024 * 1024;

    public OwnerDocumentInputValidator()
    {
        RuleFor(x => x.DocumentType)
            .NotEmpty()
            .Must(FacilityOwnerDocumentType.IsSupported)
            .WithMessage("Unknown document type.");
        RuleFor(x => x.PublicId).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SecureUrl).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SizeInBytes).GreaterThan(0).LessThanOrEqualTo(MaximumSizeInBytes);
    }
}

public sealed class FacilityInputValidator : AbstractValidator<FacilityInput>
{
    public FacilityInputValidator()
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

        // Half a coordinate points nowhere, so refuse it rather than storing a
        // pin that cannot be drawn.
        RuleFor(x => x.Longitude)
            .NotNull()
            .When(x => x.Latitude is not null)
            .WithMessage("A latitude needs a longitude.");
        RuleFor(x => x.Latitude)
            .NotNull()
            .When(x => x.Longitude is not null)
            .WithMessage("A longitude needs a latitude.");

        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrEmpty(Facility.ToSlug(name)))
            .WithMessage("The facility name needs at least one letter or number.");
    }
}

public sealed class OperatingHourInputValidator : AbstractValidator<OperatingHourInput>
{
    public OperatingHourInputValidator()
    {
        RuleFor(x => x.DayOfWeek).IsInEnum();

        // Closed is expressed by leaving both empty. One without the other is
        // an incomplete answer, not a closed day.
        RuleFor(x => x.ClosesAt)
            .NotNull()
            .When(x => x.OpensAt is not null)
            .WithMessage("An opening time needs a closing time.");
        RuleFor(x => x.OpensAt)
            .NotNull()
            .When(x => x.ClosesAt is not null)
            .WithMessage("A closing time needs an opening time.");
        RuleFor(x => x.ClosesAt)
            .GreaterThan(x => x.OpensAt)
            .When(x => x.OpensAt is not null && x.ClosesAt is not null)
            .WithMessage("A facility cannot close before it opens.");
    }
}

/// <summary>
/// A signed agreement, which is always a PDF. Scans of a multi-page contract
/// run large, so the ceiling is well above the one on a permit photo; a
/// photograph of a contract is refused because a signature has to stay legible
/// at full page size.
/// </summary>
public sealed class UploadedFileInputValidator : AbstractValidator<UploadedFileInput>
{
    public const long MaximumSizeInBytes = 50 * 1024 * 1024;
    private const string PdfContentType = "application/pdf";

    public UploadedFileInputValidator()
    {
        RuleFor(x => x.PublicId).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SecureUrl).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(100)
            .Must(contentType => string.Equals(contentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
            .WithMessage("The signed agreement must be a PDF.");
        RuleFor(x => x.SizeInBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(MaximumSizeInBytes)
            .WithMessage("The signed agreement must be 50 MB or smaller.");
    }
}

public sealed class ContractInputValidator : AbstractValidator<ContractInput>
{
    public ContractInputValidator()
    {
        // A term without the signed agreement is a claim, not a record.
        RuleFor(x => x.Document)
            .NotNull()
            .WithMessage("Attach the signed agreement.")
            .SetValidator(new UploadedFileInputValidator()!);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("A contract cannot end before it starts.");
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
