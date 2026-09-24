using FluentValidation;

namespace IcyPlay.Application.Bookings;

/// <summary>
/// What the utilization report is being asked for.
///
/// <paramref name="FacilityId"/> narrows to one venue. Null means every venue
/// this person works, which is what an owner with two buildings wants to open
/// on.
/// </summary>
public sealed record UtilizationQuery(
    DateOnly From,
    DateOnly To,
    Guid? FacilityId = null);

/// <summary>
/// How much of what a venue had open actually got used.
///
/// Everything here is in MINUTES rather than hours, and deliberately. A court
/// on ninety-minute slots does not deal in whole hours, and a report that
/// rounded each court to the nearest one would not add up to its own total.
/// The screen divides; the server does not.
/// </summary>
public sealed record UtilizationReport(
    DateOnly From,
    DateOnly To,
    int OpenMinutes,
    int InUseMinutes,
    int MaintenanceMinutes,
    int AwaitingMinutes,
    /// <summary>
    /// Court-days: one court open on one date is one. Three courts across a
    /// twenty-two day month is sixty-six, which is the figure that makes the
    /// minutes above mean anything.
    /// </summary>
    int OpenDays,
    /// <summary>
    /// Null for an attendant. The money is the owner's business, and leaving
    /// it out of the response is the only way of leaving it out — a figure
    /// hidden by the page is a figure anybody can read off the network tab.
    /// </summary>
    decimal? Rental,
    IReadOnlyCollection<CourtUtilization> Courts);

/// <summary>
/// One court — the floor, as the venue registered it — and the parts sold on
/// it.
/// </summary>
public sealed record CourtUtilization(
    Guid CourtId,
    Guid FacilityId,
    string FacilityName,
    string Name,
    /// <summary>
    /// Minutes this floor was open and sellable: its own hours or the
    /// building's, less the days it was shut, under maintenance, or outside
    /// the owner's contract — and rounded down to whole slots, because an
    /// hour the grid never offered was never on sale.
    /// </summary>
    int OpenMinutes,
    /// <summary>
    /// Minutes the floor had somebody on it. Counted ONCE however many of its
    /// parts were sold for that minute, which is what makes this the
    /// utilization figure and <see cref="SoldMinutes"/> not.
    /// </summary>
    int InUseMinutes,
    /// <summary>
    /// Minutes sold across the parts, added up. Larger than
    /// <see cref="InUseMinutes"/> whenever a divided floor ran two games side
    /// by side, which is not double counting but the point of dividing it.
    /// </summary>
    int SoldMinutes,
    /// <summary>
    /// Minutes this court would have been open, had it not been under maintenance.
    ///
    /// Kept apart from open. Nobody could have booked those hours, and folding
    /// them into the denominator would read the venue as having wasted them.
    /// Shown so that a month with a bad percentage can be explained rather than
    /// just noticed.
    /// </summary>
    int MaintenanceMinutes,
    /// <summary>Paid for and waiting on the desk. Not yet in use, not yet lost.</summary>
    int AwaitingMinutes,
    /// <summary>Dates in the range this court was actually open for business.</summary>
    int OpenDays,
    /// <summary>Dates it was under maintenance. The days behind the minutes above.</summary>
    int MaintenanceDays,
    /// <summary>
    /// The last date anything on this court was sold, up to the end of the
    /// range — which can be long before it starts. Null when nothing ever has.
    ///
    /// What turns "sold nothing this month" into "has sold nothing since
    /// March", which is the difference between a quiet month and a court
    /// nobody wants.
    /// </summary>
    DateOnly? LastSoldOn,
    decimal? Rental,
    IReadOnlyCollection<UnitUtilization> Units);

/// <summary>One bookable court: a sport, and which part of the floor.</summary>
public sealed record UnitUtilization(
    Guid BookableCourtId,
    /// <summary>"Basketball" on a whole floor, "Pickleball 2" on a divided one.</summary>
    string Label,
    string SportName,
    string SportKey,
    int SoldMinutes,
    /// <summary>
    /// Of those, the ones charged at the peak rate. What says whether the peak
    /// window is set where the demand actually is.
    /// </summary>
    int PeakMinutes,
    /// <summary>
    /// The venue has since stopped marking the floor out this way.
    ///
    /// Such a part is listed anyway when it sold hours in the period, and this
    /// says why it is there. Leaving it out instead was quietly wrong: its
    /// hours stayed in the court's own total, the rows stopped adding up to
    /// that total, and nothing on the page said what was missing.
    /// </summary>
    bool IsRetired,
    /// <summary>The last date this part was sold, up to the end of the range. Null when never.</summary>
    DateOnly? LastSoldOn,
    decimal? Rental);

public sealed class UtilizationQueryValidator : AbstractValidator<UtilizationQuery>
{
    /// <summary>
    /// A year and a day. Long enough to compare this December against last
    /// one, and short enough that nobody asks for a decade by typing a year
    /// wrong.
    /// </summary>
    public const int MostDays = 366;

    public UtilizationQueryValidator()
    {
        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .WithMessage("The report has to end on or after it starts.");
    }
}

/// <summary>
/// What the venue looks like at this moment.
///
/// **The three states add up to <paramref name="BookableCourts"/>**, and they
/// are decided in one order so that they can: under maintenance first, then
/// booked, then whatever is left is free. A part closed for work is not free,
/// whether or not anybody had it booked — and a maintenance figure that left
/// out the parts with a booking still on them would understate the closure the
/// venue actually made.
///
/// **Free is not the same as sellable.** A part with no booking of its own can
/// still be unsellable, because a clashing game has the floor: basketball
/// across the whole hall takes all three pickleball courts with it. The
/// availability grid answers "can I sell this hour"; this answers "is anybody
/// on it".
/// </summary>
public sealed record VenueSnapshot(
    /// <summary>Floors, as the venue registered them.</summary>
    int Courts,
    /// <summary>The parts those floors are sold in.</summary>
    int BookableCourts,
    /// <summary>No booking on it, and not closed for work.</summary>
    int AvailableNow,
    /// <summary>Somebody is on it, on a booking that still holds the court.</summary>
    int BookedNow,
    /// <summary>Closed for work at this moment, booked or not.</summary>
    int UnderMaintenanceNow);

/// <summary>How finely the over-time read is cut.</summary>
public static class HoursGrain
{
    public const string Day = "Day";
    public const string Week = "Week";
    public const string Month = "Month";

    public static readonly IReadOnlyCollection<string> All = [Day, Week, Month];

    public static bool IsSupported(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);
}

public sealed record HoursQuery(
    DateOnly From,
    DateOnly To,
    string Grain = HoursGrain.Day,
    Guid? FacilityId = null);

/// <summary>
/// The utilization report's own figures, cut by date rather than totalled per
/// court.
///
/// The same sums, from the same walk of the calendar. A second count would
/// give a venue two answers to one question, and the screen that draws the
/// line beside the total is exactly where that would be noticed.
/// </summary>
public sealed record HoursOverTime(
    DateOnly From,
    DateOnly To,
    string Grain,
    /// <summary>
    /// Every period in the range, whether or not anything traded in it.
    ///
    /// The rows leave out a period nobody could have traded in, so they cannot
    /// say it was there. A chart needs to — that is where the gap goes — and
    /// working the periods out on the screen would be a second copy of the
    /// bucket rule: Monday weeks, clamped ends. One copy, here.
    /// </summary>
    IReadOnlyCollection<ReportPeriod> Periods,
    /// <summary>
    /// One row per court per period. Periods a court could not have traded in
    /// at all — shut all week, or outside the owner's contract — are absent
    /// rather than zero, so a chart draws a gap where there was no offer
    /// instead of a floor where nobody bought.
    /// </summary>
    IReadOnlyCollection<CourtPeriod> Rows);

public sealed record CourtPeriod(
    DateOnly Starts,
    DateOnly Ends,
    Guid CourtId,
    Guid FacilityId,
    string FacilityName,
    string CourtName,
    int OpenMinutes,
    int SoldMinutes,
    int MaintenanceMinutes,
    /// <summary>
    /// The parts this court is sold in — plus any retired part that still sold
    /// in the period, so <see cref="PartsSold"/> can never be the larger.
    /// </summary>
    int Parts,
    /// <summary>
    /// Of those, how many had a confirmed booking at some point in the period.
    /// <see cref="Parts"/> less this is how many sat with no booking at all,
    /// which is what the not-sold line counts.
    /// </summary>
    int PartsSold);

/// <summary>One bucket of an over-time read, clamped to the range asked for.</summary>
public sealed record ReportPeriod(DateOnly Starts, DateOnly Ends);

/// <summary>
/// How many bookings customers moved, and why.
///
/// A move counts on the day it went through, on the venue's clock: a free move
/// the moment it was asked for, an upgrade the moment the desk approved it.
/// The day the booking is for is a different question and not this one.
/// </summary>
public sealed record MovesReport(
    DateOnly From,
    DateOnly To,
    string Grain,
    /// <summary>
    /// Every period in the range, each with its counts — zero where nothing
    /// moved. Unlike hours there is no "closed" here: a customer can move a
    /// booking on a day the venue is shut.
    /// </summary>
    IReadOnlyCollection<MovesPeriod> Periods,
    /// <summary>Every reason on the list, most given first, then the moves nobody was asked about.</summary>
    IReadOnlyCollection<ReasonCount> Reasons,
    /// <summary>All the moves in the range, not only the ones listed below.</summary>
    int Total,
    /// <summary>
    /// The moves themselves, newest first, up to <see cref="MovesReport.Listed"/>.
    /// The counts above are always the whole range.
    /// </summary>
    IReadOnlyCollection<MovedBooking> Moves)
{
    public const int Listed = 200;
}

public sealed record MovesPeriod(
    DateOnly Starts,
    DateOnly Ends,
    /// <summary>The same price or cheaper, done the moment it was asked.</summary>
    int Free,
    /// <summary>Paid for and approved at the desk.</summary>
    int Upgrade,
    /// <summary>Every reason on the list, zero included, in the list's own order; then the unasked.</summary>
    IReadOnlyCollection<ReasonCount> Reasons);

/// <summary>
/// One reason and how often it was given. <see cref="Reason"/> is null for the
/// moves made before customers were asked, which a report shows as not asked
/// rather than guessing.
/// </summary>
public sealed record ReasonCount(string? Reason, int Count);

public sealed record MovedBooking(
    Guid BookingId,
    DateTimeOffset MovedAt,
    /// <summary>The day it moved, on the venue's clock — the day it is counted in.</summary>
    DateOnly MovedOn,
    string CustomerName,
    string FacilityName,
    string? FromCourtName,
    string? ToCourtName,
    string Kind,
    string? Reason,
    string? ReasonNote);
