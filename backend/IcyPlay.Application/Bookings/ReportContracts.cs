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
    int IdleMinutes,
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
    /// Open and nobody on it. <see cref="OpenMinutes"/> less
    /// <see cref="InUseMinutes"/>, worked out here so a screen cannot arrive at
    /// a negative by subtracting the wrong pair.
    /// </summary>
    int IdleMinutes,
    /// <summary>
    /// Minutes this court would have been open, had it not been under maintenance.
    ///
    /// Kept apart from both open and idle. A court under maintenance was
    /// not idle — nobody could have booked it — and folding those hours into
    /// the denominator would read the venue as having wasted them. Shown so
    /// that a month with a bad percentage can be explained rather than just
    /// noticed.
    /// </summary>
    int MaintenanceMinutes,
    /// <summary>Paid for and waiting on the desk. Not yet in use, not yet lost.</summary>
    int AwaitingMinutes,
    /// <summary>Dates in the range this court was actually open for business.</summary>
    int OpenDays,
    /// <summary>Dates it was under maintenance. The days behind the minutes above.</summary>
    int MaintenanceDays,
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
