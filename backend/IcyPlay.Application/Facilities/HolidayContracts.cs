using FluentValidation;
using IcyPlay.Application.Audit;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Application.Facilities;

public sealed record HolidayListItem(
    Guid Id,
    string Name,
    DateOnly Date,
    string Kind,
    bool RepeatsAnnually,
    bool IsActive,
    /// <summary>
    /// The next day this falls on, from today. Null for a moving holiday whose
    /// date has already passed, which is the signal that it needs adding again
    /// for the coming year.
    /// </summary>
    DateOnly? NextOccurrence);

public sealed record CreateHolidayRequest(
    string Name,
    DateOnly Date,
    string Kind,
    bool RepeatsAnnually);

public sealed record UpdateHolidayRequest(
    string Name,
    DateOnly Date,
    string Kind,
    bool RepeatsAnnually);

/// <summary>
/// The holiday calendar, managed rather than compiled in. Half the Philippine
/// calendar moves each year, so a venue charging a holiday rate correctly is a
/// matter of data, not of a deploy.
/// </summary>
public interface IHolidayService
{
    Task<IReadOnlyCollection<HolidayListItem>> ListAsync(
        bool includeRetired,
        CancellationToken cancellationToken);

    Task<CourtResult<Guid>> CreateAsync(
        CreateHolidayRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    Task<CourtResult<bool>> UpdateAsync(
        Guid id,
        UpdateHolidayRequest request,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retires rather than deletes. A booking priced as a holiday needs the day
    /// that made it one to still be there when the receipt is questioned.
    /// </summary>
    Task<CourtResult<bool>> SetActiveAsync(
        Guid id,
        bool isActive,
        AuditActor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Whether the given day is a holiday. This is what a booking asks before
    /// it decides which of a court's rates applies.
    /// </summary>
    Task<bool> IsHolidayAsync(DateOnly day, CancellationToken cancellationToken);
}

public sealed class CreateHolidayRequestValidator : AbstractValidator<CreateHolidayRequest>
{
    public CreateHolidayRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(HolidayKind.IsSupported)
            .WithMessage("A holiday is either Regular or Special non-working.");
    }
}

public sealed class UpdateHolidayRequestValidator : AbstractValidator<UpdateHolidayRequest>
{
    public UpdateHolidayRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(HolidayKind.IsSupported)
            .WithMessage("A holiday is either Regular or Special non-working.");
    }
}
