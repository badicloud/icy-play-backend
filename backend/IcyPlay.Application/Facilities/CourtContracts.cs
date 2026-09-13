using FluentValidation;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// One submit from the court wizard. The facility is either picked from the
/// ones the owner already has, or created here alongside the court — and
/// either way it is one transaction, so an abandoned wizard leaves neither a
/// stray facility nor a court without one.
/// </summary>
public sealed record CreateCourtRequest(
    Guid FacilityOwnerId,
    /// <summary>Set when an existing facility was chosen.</summary>
    Guid? FacilityId,
    /// <summary>Set when a new facility is being added in the same step.</summary>
    NewFacilityInput? NewFacility,
    CourtInput Court);

public sealed record NewFacilityInput(
    FacilityInput Details,
    IReadOnlyCollection<OperatingHourInput> OperatingHours,
    IReadOnlyCollection<PhotoInput> Photos);

public sealed record CourtInput(
    string Name,
    int DisplayOrder,
    string? Description,
    /// <summary>At least one. A court that accommodates no sport cannot be booked.</summary>
    IReadOnlyCollection<Guid> SportIds,
    /// <summary>Must be one of the above: it is what the court is listed as.</summary>
    Guid PrimarySportId,
    string VenueType,
    string? Surface,
    bool HasLighting,
    string? SizeLabel,
    int? Capacity,
    string? Equipment,
    int SlotLengthMinutes,
    int MinimumDurationMinutes,
    int BufferMinutes,
    bool UsesFacilityHours,
    /// <summary>Only read when the court has opted out of the facility's hours.</summary>
    IReadOnlyCollection<OperatingHourInput> OperatingHours,
    IReadOnlyCollection<PhotoInput> Photos);

public sealed record CreatedCourtResponse(Guid CourtId, Guid FacilityId, string FacilityName);

/// <summary>
/// One picture already in Cloudinary. Exactly one per subject carries the cover
/// flag: it is what the booking portal and the booking list show.
/// </summary>
public sealed record PhotoInput(
    string PublicId,
    string SecureUrl,
    string? Caption,
    int DisplayOrder,
    bool IsCover);

public sealed record PhotoItem(
    Guid Id,
    string PublicId,
    string SecureUrl,
    string? Caption,
    int DisplayOrder,
    bool IsCover);

public sealed record CourtListItem(
    Guid Id,
    Guid FacilityId,
    string FacilityName,
    string Name,
    int DisplayOrder,
    string? Description,
    string VenueType,
    string? Surface,
    bool HasLighting,
    string? SizeLabel,
    int? Capacity,
    string? Equipment,
    int SlotLengthMinutes,
    int MinimumDurationMinutes,
    int BufferMinutes,
    bool UsesFacilityHours,
    bool IsActive,
    IReadOnlyCollection<CourtSportItem> Sports,
    IReadOnlyCollection<PhotoItem> Photos,
    IReadOnlyCollection<FacilityOperatingHourDetail> OperatingHours,
    /// <summary>Null when nothing is closed. Carries which level closed it.</summary>
    MaintenanceStatus? Maintenance,
    DateTimeOffset CreatedAt);

public sealed record CourtSportItem(Guid SportId, string Key, string Name, string Category, bool IsPrimary);

/// <summary>
/// Why a court is out of service, and at which level. The level matters: a
/// closure set on the facility cannot be lifted from the court, and an admin
/// who is not told that goes looking for a button that is not there.
/// </summary>
public sealed record MaintenanceStatus(
    Guid PeriodId,
    bool AppliesToWholeFacility,
    string Reason,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt);

public sealed record SetMaintenanceRequest(
    DateTimeOffset StartsAt,
    /// <summary>Null means until further notice.</summary>
    DateTimeOffset? EndsAt,
    string Reason);

/// <summary>
/// One row of the facility inventory: every venue on the platform, whoever owns
/// it. Bookable is the column that matters, because it answers the question the
/// list exists for — what can a customer actually book right now.
/// </summary>
public sealed record FacilityInventoryItem(
    Guid Id,
    string Name,
    string Slug,
    string City,
    string Province,
    Guid FacilityOwnerId,
    string BusinessName,
    /// <summary>Derived from the owner's contracts, never stored.</summary>
    string OwnerStatus,
    bool IsActive,
    int CourtCount,
    int ActiveCourtCount,
    /// <summary>Null when nothing is closed.</summary>
    MaintenanceStatus? Maintenance,
    /// <summary>What the booking list would show. Null when nobody has set one.</summary>
    string? CoverPhotoUrl,
    /// <summary>
    /// True only when the owner has a live contract, the facility is active,
    /// nothing is under maintenance, and there is at least one active court.
    /// </summary>
    bool IsBookable,
    DateTimeOffset CreatedAt);

public sealed record FacilityInventoryQuery(
    string? Search = null,
    Guid? FacilityOwnerId = null,
    int Page = 1,
    int PageSize = 20);

public sealed record SportListItem(
    Guid Id,
    string Key,
    string Name,
    string Category,
    int DisplayOrder,
    bool IsActive,
    /// <summary>How many courts list it, so retiring one is a decision with a number attached.</summary>
    int CourtCount);

public sealed record CreateSportRequest(string Name, string Category, int DisplayOrder);

public sealed record UpdateSportRequest(string Name, string Category, int DisplayOrder);

public enum CourtFailure
{
    None,
    FacilityOwnerNotFound,
    FacilityNotFound,
    CourtNotFound,
    FacilityChoiceInvalid,
    UnknownSport,
    UnknownAmenity,
    UnknownTimeZone,
    PrimarySportNotSelected,
    DuplicateSportKey,
    SportInUse,
    AlreadyUnderMaintenance,
    MaintenanceNotFound,
    UntrustedPhotoUrl,
    TooManyCovers
}

public sealed record CourtResult<T>(T? Value, CourtFailure Failure = CourtFailure.None)
{
    public bool Succeeded => Failure == CourtFailure.None;
    public static CourtResult<T> Success(T value) => new(value);
    public static CourtResult<T> Fail(CourtFailure failure) => new(default, failure);
}

public sealed class CourtInputValidator : AbstractValidator<CourtInput>
{
    public CourtInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);

        RuleFor(x => x.SportIds)
            .NotEmpty()
            .WithMessage("Pick at least one sport this court can take.");
        RuleFor(x => x.PrimarySportId)
            .Must((input, primary) => input.SportIds.Contains(primary))
            .WithMessage("The main sport has to be one of the sports selected.");

        RuleFor(x => x.VenueType)
            .NotEmpty()
            .Must(CourtVenueType.IsSupported)
            .WithMessage("Venue type must be Indoor, Covered or Outdoor.");
        RuleFor(x => x.Surface)
            .Must(surface => surface is null || CourtSurface.IsSupported(surface))
            .WithMessage("That surface is not one we recognise.");
        RuleFor(x => x.SizeLabel).MaximumLength(100);
        RuleFor(x => x.Equipment).MaximumLength(500);
        RuleFor(x => x.Capacity).GreaterThan(0).When(x => x.Capacity is not null);

        RuleFor(x => x.SlotLengthMinutes)
            .GreaterThan(0)
            .LessThanOrEqualTo(24 * 60);
        RuleFor(x => x.MinimumDurationMinutes)
            .GreaterThanOrEqualTo(x => x.SlotLengthMinutes)
            .WithMessage("The minimum booking cannot be shorter than one slot.");
        RuleFor(x => x.MinimumDurationMinutes)
            .Must((input, minimum) =>
                input.SlotLengthMinutes > 0 && minimum % input.SlotLengthMinutes == 0)
            .WithMessage("The minimum booking has to be a whole number of slots.");
        RuleFor(x => x.BufferMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);

        RuleForEach(x => x.Photos).SetValidator(new PhotoInputValidator());
        // More than one cover is two answers to a question that has one.
        RuleFor(x => x.Photos)
            .Must(photos => photos.Count(photo => photo.IsCover) <= 1)
            .WithMessage("Only one photo can be the cover.");

        RuleForEach(x => x.OperatingHours).SetValidator(new OperatingHourInputValidator());
        RuleFor(x => x.OperatingHours)
            .Must(hours => hours.Select(hour => hour.DayOfWeek).Distinct().Count() == hours.Count)
            .WithMessage("Each day of the week can appear only once.");
        // Opting out of the facility's hours and then supplying none would
        // leave a court with no hours at all.
        RuleFor(x => x.OperatingHours)
            .NotEmpty()
            .When(x => !x.UsesFacilityHours)
            .WithMessage("A court with its own hours needs them filled in.");
    }
}

public sealed class PhotoInputValidator : AbstractValidator<PhotoInput>
{
    public PhotoInputValidator()
    {
        RuleFor(x => x.PublicId).NotEmpty().MaximumLength(300);
        RuleFor(x => x.SecureUrl).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Caption).MaximumLength(300);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateCourtRequestValidator : AbstractValidator<CreateCourtRequest>
{
    public CreateCourtRequestValidator()
    {
        RuleFor(x => x.FacilityOwnerId).NotEmpty();
        RuleFor(x => x.Court).NotNull().SetValidator(new CourtInputValidator());

        // Exactly one: a request naming both is ambiguous, and one naming
        // neither has nowhere to put the court.
        RuleFor(x => x)
            .Must(request => (request.FacilityId is not null) ^ (request.NewFacility is not null))
            .WithMessage("Choose an existing facility, or add a new one. Not both, and not neither.");

        When(x => x.NewFacility is not null, () =>
        {
            RuleFor(x => x.NewFacility!.Details).SetValidator(new FacilityInputValidator());
            RuleForEach(x => x.NewFacility!.OperatingHours)
                .SetValidator(new OperatingHourInputValidator());
            RuleFor(x => x.NewFacility!.OperatingHours)
                .Must(hours => hours.Select(hour => hour.DayOfWeek).Distinct().Count() == hours.Count)
                .WithMessage("Each day of the week can appear only once.");
            RuleForEach(x => x.NewFacility!.Photos).SetValidator(new PhotoInputValidator());
            RuleFor(x => x.NewFacility!.Photos)
                .Must(photos => photos.Count(photo => photo.IsCover) <= 1)
                .WithMessage("Only one photo can be the cover.");
        });
    }
}

public sealed class SetMaintenanceRequestValidator : AbstractValidator<SetMaintenanceRequest>
{
    public SetMaintenanceRequestValidator()
    {
        // The reason is what the affected customers are told, so it is never
        // optional.
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.EndsAt)
            .GreaterThan(x => x.StartsAt)
            .When(x => x.EndsAt is not null)
            .WithMessage("Maintenance cannot end before it starts.");
    }
}

public sealed class CreateSportRequestValidator : AbstractValidator<CreateSportRequest>
{
    public CreateSportRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(SportCategory.IsSupported)
            .WithMessage($"Category must be one of: {string.Join(", ", SportCategory.All)}.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateSportRequestValidator : AbstractValidator<UpdateSportRequest>
{
    public UpdateSportRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(SportCategory.IsSupported)
            .WithMessage($"Category must be one of: {string.Join(", ", SportCategory.All)}.");
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
