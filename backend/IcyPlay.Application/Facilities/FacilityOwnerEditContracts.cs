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

/// <summary>
/// Where a venue takes its money, and how long it will hold a court while
/// waiting for it.
///
/// Both halves of the GCash details are optional on their own — a venue with
/// only a QR code, or only a number, can still be paid — but a venue with
/// neither cannot take a booking to the end, which is worth saying on the
/// screen rather than discovering at checkout.
/// </summary>
public sealed record UpdatePaymentDetailsRequest(
    string? GcashNumber,
    string? GcashAccountName,
    string? GcashQrCodeUrl,
    int PartialBookingExpiryMinutes,
    string? Reason);

public sealed class UpdatePaymentDetailsRequestValidator
    : AbstractValidator<UpdatePaymentDetailsRequest>
{
    public UpdatePaymentDetailsRequestValidator()
    {
        RuleFor(x => x.GcashNumber).MaximumLength(30);
        RuleFor(x => x.GcashAccountName).MaximumLength(150);
        RuleFor(x => x.GcashQrCodeUrl).MaximumLength(1000);
        RuleFor(x => x.PartialBookingExpiryMinutes)
            .Must(IcyPlay.Domain.Bookings.PaymentHold.IsSupported)
            .WithMessage(
                $"A hold has to be between {IcyPlay.Domain.Bookings.PaymentHold.MinimumMinutes} " +
                $"and {IcyPlay.Domain.Bookings.PaymentHold.MaximumMinutes} minutes.");
        // A number without a name gives the customer nothing to check the
        // recipient against before the money moves.
        RuleFor(x => x.GcashAccountName)
            .NotEmpty()
            .When(x => !string.IsNullOrWhiteSpace(x.GcashNumber))
            .WithMessage("Say whose GCash account the number belongs to.");
    }
}

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
    IReadOnlyCollection<PhotoInput> Photos,
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

/// <summary>
/// What IcyPlay charges under one contract term. Its own request, because a
/// rate is renegotiated far more often than a term is renewed, and routing it
/// through a renewal would mean re-attaching the signed agreement to change a
/// percentage.
/// </summary>
public sealed record UpdateContractRatesRequest(
    decimal PlatformHourlyRate,
    decimal CommissionPercentage,
    string? Reason);

/// <summary>
/// Corrects the dates of a term that already exists. A start date typed wrong
/// leaves an owner invisible to customers for a year, and the only way to see
/// that is to be able to look at it and change it.
/// </summary>
public sealed record UpdateContractTermRequest(
    DateOnly StartDate,
    DateOnly EndDate,
    string? Notes,
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
    UntrustedContractDocument,
    UntrustedPhotoUrl,
    /// <summary>The GCash QR code is not a secure link on the configured Cloudinary account.</summary>
    UntrustedAssetUrl
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
        RuleFor(x => x.TimeZone).NotEmpty().MaximumLength(100).MustBeARealTimeZone();
        RuleFor(x => x.ContactPhone).MaximumLength(50);
        RuleFor(x => x.ContactEmail).EmailAddress().MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);
        RuleFor(x => x.Longitude).NotNull().When(x => x.Latitude is not null)
            .WithMessage("A latitude needs a longitude.");
        RuleFor(x => x.Latitude).NotNull().When(x => x.Longitude is not null)
            .WithMessage("A longitude needs a latitude.");
        RuleForEach(x => x.Photos).SetValidator(new PhotoInputValidator());
        RuleFor(x => x.Photos)
            .Must(photos => photos.Count(photo => photo.IsCover) <= 1)
            .WithMessage("Only one photo can be the cover.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class UpdateContractTermRequestValidator
    : AbstractValidator<UpdateContractTermRequest>
{
    public UpdateContractTermRequestValidator()
    {
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("A term cannot end before it starts.");
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class UpdateContractRatesRequestValidator
    : AbstractValidator<UpdateContractRatesRequest>
{
    public UpdateContractRatesRequestValidator()
    {
        // Zero is allowed on both: a venue onboarded as a favour pays nothing,
        // and refusing to record that would only push it into a side agreement
        // nobody can see.
        RuleFor(x => x.PlatformHourlyRate)
            .InclusiveBetween(0, 10_000)
            .WithMessage("The platform rate has to be between 0 and 10,000 pesos an hour.");
        RuleFor(x => x.CommissionPercentage)
            .InclusiveBetween(0, 100)
            .WithMessage("Commission has to be between 0 and 100 per cent.");
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
