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
    IReadOnlyCollection<CourtSportInput> Sports,
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

/// <summary>
/// One sport on a court, and how many playable courts it makes when set up for
/// it. A full basketball court is three pickleball courts across, and each of
/// those is booked and paid for on its own.
/// </summary>
public sealed record CourtSportInput(Guid SportId, int Divisions);

/// <summary>
/// How the court is marked out, on its own. Changing a division count is a
/// small, frequent correction — a floor gets re-marked — and making it go
/// through the whole court would put every other field at risk to change one
/// number. Sports left out keep what they had.
/// </summary>
public sealed record UpdateCourtDivisionsRequest(
    IReadOnlyCollection<CourtSportInput> Sports,
    string? Reason);

public sealed record CreatedCourtResponse(Guid CourtId, Guid FacilityId, string FacilityName);

/// <summary>
/// Editing answers the court whole, the way the wizard created it: the same
/// shape in, so a screen that can add a court can also correct one. Whether
/// the court is active is separate, because taking one off the booking portal
/// is a decision rather than a detail.
/// </summary>
public sealed record UpdateCourtRequest(
    CourtInput Court,
    bool IsActive,
    string? Reason);

/// <summary>
/// One picture already in Cloudinary. Exactly one per subject carries the cover
/// flag: it is what the booking portal and the booking list show.
/// </summary>
public sealed record PhotoInput(
    string PublicId,
    string SecureUrl,
    string? Caption,
    int DisplayOrder,
    bool IsCover,
    /// <summary>
    /// The sport this shows the court marked out for, when it shows one in
    /// particular. Null — the ordinary case — is a picture of the floor that
    /// suits whatever is played on it. Must be a sport the court offers.
    /// </summary>
    Guid? SportId = null);

public sealed record PhotoItem(
    Guid Id,
    string PublicId,
    string SecureUrl,
    string? Caption,
    int DisplayOrder,
    bool IsCover,
    Guid? SportId = null);

public sealed record CourtListItem(
    Guid Id,
    Guid FacilityId,
    string FacilityName,
    Guid FacilityOwnerId,
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
    /// <summary>When the peak rate applies. Null until a peak rate is set.</summary>
    TimeOnly? PeakStartsAt,
    TimeOnly? PeakEndsAt,
    bool PeakOnWeekdays,
    bool PeakOnWeekends,
    IReadOnlyCollection<CourtSportItem> Sports,
    /// <summary>
    /// What this court actually sells, one row per playable part. Sent
    /// alongside the sports rather than worked out from their division counts,
    /// so the console shows the same five things the booking engine knows
    /// about rather than its own arithmetic.
    /// </summary>
    IReadOnlyCollection<BookableCourtItem> BookableCourts,
    IReadOnlyCollection<PhotoItem> Photos,
    IReadOnlyCollection<FacilityOperatingHourDetail> OperatingHours,
    /// <summary>Null when nothing is closed. Carries which level closed it.</summary>
    MaintenanceStatus? Maintenance,
    DateTimeOffset CreatedAt);

/// <summary>
/// One thing a customer can book. Retired parts are left out: this answers what
/// is on sale, and a part the floor no longer has is only of interest to the
/// bookings already taken against it.
/// </summary>
public sealed record BookableCourtItem(
    Guid Id,
    Guid SportId,
    string SportName,
    /// <summary>Which part this is. One when the court is played whole.</summary>
    int DivisionNumber,
    /// <summary>What a customer sees, derived the same way the listing derives it.</summary>
    string Name,
    /// <summary>"Whole" or "Divided".</summary>
    string Kind);

public sealed record CourtSportItem(
    Guid SportId,
    string Key,
    string Name,
    string Category,
    /// <summary>"Sport" or "Event", so a customer looking for a game is not offered a wedding.</summary>
    string Kind,
    bool IsPrimary,
    /// <summary>How many playable courts this one makes for this sport. One means whole.</summary>
    int Divisions,
    /// <summary>Null until a price has been set. The other three fall back to it.</summary>
    decimal? StandardHourlyRate,
    decimal? PeakHourlyRate,
    decimal? WeekendRate,
    decimal? HolidayRate);

/// <summary>
/// What one sport costs on one court. The console can fill every sport from a
/// single set of rates, but each is still sent on its own: applying one price
/// to all of them is a convenience, not a constraint the data should carry.
/// </summary>
public sealed record SportPricingInput(
    Guid SportId,
    decimal? StandardHourlyRate,
    decimal? PeakHourlyRate,
    decimal? WeekendRate,
    decimal? HolidayRate);

/// <summary>
/// When the peak rate applies. On the court rather than on each sport, because
/// a venue is busy at the same hours whatever is being played on it.
/// </summary>
public sealed record PeakWindowInput(
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    bool OnWeekdays,
    bool OnWeekends);

public sealed record UpdateCourtPricingRequest(
    IReadOnlyCollection<SportPricingInput> Sports,
    PeakWindowInput? PeakWindow,
    string? Reason);

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

/// <summary>
/// One court, read across the whole platform rather than through its facility.
/// An admin correcting a court should not have to remember which venue it is in
/// to find it.
/// </summary>
public sealed record CourtInventoryItem(
    Guid Id,
    string Name,
    int DisplayOrder,
    bool IsActive,
    Guid FacilityId,
    string FacilityName,
    Guid FacilityOwnerId,
    string BusinessName,
    string City,
    string Province,
    string VenueType,
    string? CoverPhotoUrl,
    IReadOnlyCollection<CourtSportItem> Sports,
    /// <summary>Every division of every sport: what a customer could actually book here.</summary>
    int BookableUnits,
    /// <summary>Null when nothing is closed. Carries which level closed it.</summary>
    MaintenanceStatus? Maintenance);

public sealed record CourtInventoryQuery(
    string? Search = null,
    Guid? FacilityOwnerId = null,
    Guid? FacilityId = null,
    int Page = 1,
    int PageSize = 20);

public sealed record SportListItem(
    Guid Id,
    string Key,
    string Name,
    string Category,
    /// <summary>"Sport" for something played, "Event" for something held.</summary>
    string Kind,
    int DisplayOrder,
    bool IsActive,
    /// <summary>How many courts list it, so retiring one is a decision with a number attached.</summary>
    int CourtCount,
    /// <summary>The stock picture, shown when a court has none of its own.</summary>
    string? ImagePublicId = null,
    string? ImageSecureUrl = null);

public sealed record CreateSportRequest(
    string Name,
    string Category,
    int DisplayOrder,
    string Kind,
    /// <summary>Optional stock picture. Both parts, or neither.</summary>
    string? ImagePublicId = null,
    string? ImageSecureUrl = null);

public sealed record UpdateSportRequest(
    string Name,
    string Category,
    int DisplayOrder,
    string Kind,
    string? ImagePublicId = null,
    string? ImageSecureUrl = null);

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
    TooManyCovers,
    PhotoSportNotOnCourt,
    DuplicateHoliday,
    HolidayNotFound,
    PeakWindowOutsideHours,
    PeakWindowOnClosedDays
}

public sealed record CourtResult<T>(T? Value, CourtFailure Failure = CourtFailure.None)
{
    public bool Succeeded => Failure == CourtFailure.None;
    public static CourtResult<T> Success(T value) => new(value);
    public static CourtResult<T> Fail(CourtFailure failure) => new(default, failure);
}

public sealed class UpdateCourtDivisionsRequestValidator
    : AbstractValidator<UpdateCourtDivisionsRequest>
{
    public UpdateCourtDivisionsRequestValidator()
    {
        RuleFor(x => x.Sports).NotEmpty();
        RuleForEach(x => x.Sports).SetValidator(new CourtSportInputValidator());
        RuleFor(x => x.Sports)
            .Must(sports => sports.Select(sport => sport.SportId).Distinct().Count() == sports.Count)
            .WithMessage("Each sport can appear only once.");
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CourtSportInputValidator : AbstractValidator<CourtSportInput>
{
    /// <summary>
    /// High enough for a hall marked out end to end, low enough that a typo
    /// does not silently create a hundred bookable courts.
    /// </summary>
    private const int MaximumDivisions = 12;

    public CourtSportInputValidator()
    {
        RuleFor(x => x.Divisions)
            .InclusiveBetween(1, MaximumDivisions)
            .WithMessage($"A court divides into between 1 and {MaximumDivisions} courts.");
    }
}

public sealed class UpdateCourtPricingRequestValidator : AbstractValidator<UpdateCourtPricingRequest>
{
    public UpdateCourtPricingRequestValidator()
    {
        RuleForEach(x => x.Sports).SetValidator(new SportPricingInputValidator());
        RuleFor(x => x.Sports)
            .Must(sports => sports.Select(sport => sport.SportId).Distinct().Count() == sports.Count)
            .WithMessage("Each sport can appear only once.");
        RuleFor(x => x.Reason).MaximumLength(500);

        // A peak rate with no window can never be charged, so the two are
        // asked for together rather than letting one sit uselessly without
        // the other.
        RuleFor(x => x.PeakWindow)
            .NotNull()
            .When(x => x.Sports.Any(sport => sport.PeakHourlyRate is not null))
            .WithMessage("Say when the peak rate applies before setting one.");

        RuleFor(x => x.PeakWindow!)
            .SetValidator(new PeakWindowInputValidator())
            .When(x => x.PeakWindow is not null);
    }
}

public sealed class PeakWindowInputValidator : AbstractValidator<PeakWindowInput>
{
    public PeakWindowInputValidator()
    {
        // Either the window is set whole or it is not set at all: half of one
        // describes no stretch of time.
        RuleFor(x => x.EndsAt)
            .NotNull()
            .When(x => x.StartsAt is not null)
            .WithMessage("A peak window needs an end time.");
        RuleFor(x => x.StartsAt)
            .NotNull()
            .When(x => x.EndsAt is not null)
            .WithMessage("A peak window needs a start time.");

        RuleFor(x => x.EndsAt)
            .Must((window, endsAt) => endsAt > window.StartsAt)
            .When(x => x.StartsAt is not null && x.EndsAt is not null)
            .WithMessage("The peak window has to end after it starts.");

        RuleFor(x => x.OnWeekdays)
            .Must((window, _) => window.OnWeekdays || window.OnWeekends)
            .When(x => x.StartsAt is not null)
            .WithMessage("Apply the peak window to weekdays, weekends, or both.");
    }
}

public sealed class SportPricingInputValidator : AbstractValidator<SportPricingInput>
{
    /// <summary>
    /// High enough that no real court hits it, low enough that a mistyped rate
    /// with three extra zeros is caught before it reaches a customer.
    /// </summary>
    private const decimal MaximumHourlyRate = 100_000m;

    public SportPricingInputValidator()
    {
        RuleFor(x => x.StandardHourlyRate)
            .InclusiveBetween(0, MaximumHourlyRate)
            .When(x => x.StandardHourlyRate is not null);
        RuleFor(x => x.PeakHourlyRate)
            .InclusiveBetween(0, MaximumHourlyRate)
            .When(x => x.PeakHourlyRate is not null);
        RuleFor(x => x.WeekendRate)
            .InclusiveBetween(0, MaximumHourlyRate)
            .When(x => x.WeekendRate is not null);
        RuleFor(x => x.HolidayRate)
            .InclusiveBetween(0, MaximumHourlyRate)
            .When(x => x.HolidayRate is not null);

        // A special rate with nothing to be special against would be charged as
        // the standard one anyway, which is not what typing it meant.
        RuleFor(x => x.StandardHourlyRate)
            .NotNull()
            .When(x =>
                x.PeakHourlyRate is not null ||
                x.WeekendRate is not null ||
                x.HolidayRate is not null)
            .WithMessage("Set the standard rate before the peak, weekend or holiday one.");
    }
}

public sealed class UpdateCourtRequestValidator : AbstractValidator<UpdateCourtRequest>
{
    public UpdateCourtRequestValidator()
    {
        RuleFor(x => x.Court).NotNull().SetValidator(new CourtInputValidator());
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class CourtInputValidator : AbstractValidator<CourtInput>
{
    public CourtInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(0);

        RuleFor(x => x.Sports)
            .NotEmpty()
            .WithMessage("Pick at least one sport this court can take.");
        RuleFor(x => x.Sports)
            .Must(sports => sports.Select(sport => sport.SportId).Distinct().Count() == sports.Count)
            .WithMessage("Each sport can appear only once.");
        RuleFor(x => x.PrimarySportId)
            .Must((input, primary) => input.Sports.Any(sport => sport.SportId == primary))
            .WithMessage("The main sport has to be one of the sports selected.");
        RuleForEach(x => x.Sports).SetValidator(new CourtSportInputValidator());

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
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(ActivityKind.IsSupported)
            .WithMessage("An entry is either a Sport or an Event.");
        RuleFor(x => x.ImagePublicId).MaximumLength(300);
        RuleFor(x => x.ImageSecureUrl).MaximumLength(1000);
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
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(ActivityKind.IsSupported)
            .WithMessage("An entry is either a Sport or an Event.");
        RuleFor(x => x.ImagePublicId).MaximumLength(300);
        RuleFor(x => x.ImageSecureUrl).MaximumLength(1000);
    }
}
