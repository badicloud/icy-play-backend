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
/// What became of one row of an imported file.
///
/// Every row is answered, including the ones that changed nothing: an import
/// that says "14 added" and nothing else leaves the reader to work out what
/// happened to the other six, and the usual answer — they were already there —
/// is the one they most need to hear.
/// </summary>
public sealed record HolidayImportRow(
    /// <summary>The row in the sheet, as Excel numbers it, so it can be found.</summary>
    int Row,
    string? Name,
    DateOnly? Date,
    HolidayImportOutcome Outcome,
    /// <summary>Why it was skipped or rejected. Null when it was added.</summary>
    string? Reason);

public enum HolidayImportOutcome
{
    Added,
    /// <summary>Already on the calendar, or repeated further up the same file.</summary>
    Skipped,
    /// <summary>Could not be read as a holiday at all.</summary>
    Rejected
}

/// <summary>
/// What an import did, row by row and in total.
/// </summary>
public sealed record HolidayImportResult(
    int Added,
    int Skipped,
    int Rejected,
    IReadOnlyCollection<HolidayImportRow> Rows);

public enum HolidayImportFailure
{
    None,
    /// <summary>Not a workbook, or one this cannot open.</summary>
    Unreadable,
    /// <summary>Opened, but the header is not the template's.</summary>
    NotTheTemplate,
    /// <summary>A header and nothing under it.</summary>
    Empty,
    TooManyRows
}

public sealed record HolidayImportOutput(
    HolidayImportResult? Result,
    HolidayImportFailure Failure = HolidayImportFailure.None)
{
    public bool Succeeded => Failure == HolidayImportFailure.None;
    public static HolidayImportOutput Success(HolidayImportResult result) => new(result);
    public static HolidayImportOutput Fail(HolidayImportFailure failure) => new(null, failure);
}

/// <summary>
/// The shape of the template, in one place: the sheet that is handed out and
/// the sheet that is read back are the same sheet, so a column renamed here
/// moves both at once.
/// </summary>
public static class HolidayTemplate
{
    public const string SheetName = "Holidays";
    public const string FileName = "icyplay-holidays-template.xlsx";

    public static readonly IReadOnlyList<string> Columns =
        ["Name", "Date", "Kind", "Repeats annually"];

    /// <summary>
    /// A ceiling on what one upload may carry. The Philippine calendar has
    /// about twenty holidays a year; a file with thousands of rows is a mistake
    /// rather than a request, and reading it would hold a transaction open.
    /// </summary>
    public const int MostRows = 500;
}

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

    /// <summary>
    /// The empty sheet an admin fills in, as xlsx bytes.
    /// </summary>
    byte[] Template();

    /// <summary>
    /// Reads a filled-in template and adds what is new, leaving what is already
    /// there alone. Answers every row rather than a count, because "already on
    /// the calendar" is the outcome an importer most needs to see.
    /// </summary>
    Task<HolidayImportOutput> ImportAsync(
        Stream workbook,
        AuditActor actor,
        CancellationToken cancellationToken);
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
