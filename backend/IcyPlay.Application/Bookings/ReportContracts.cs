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

    /// <summary>Calendar quarters: January, April, July and October.</summary>
    public const string Quarter = "Quarter";

    /// <summary>January to June, and July to December.</summary>
    public const string Half = "Half";

    public const string Year = "Year";

    public static readonly IReadOnlyCollection<string> All = [Day, Week, Month, Quarter, Half, Year];

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

/// <summary>
/// How many payments the desk turned down, against how many it checked, and why.
///
/// A payment counts on the day it was answered, on the venue's clock: a refusal
/// on the day it was refused, a confirmation on the day it was confirmed. The
/// two together are what the desk checked that day.
/// </summary>
public sealed record DeclinesReport(
    DateOnly From,
    DateOnly To,
    string Grain,
    /// <summary>Every period in the range, zero included.</summary>
    IReadOnlyCollection<DeclinesPeriod> Periods,
    /// <summary>
    /// The range's reasons, most given first; a reason nobody gave is left out.
    /// Null counts the refusals from before the desk picked from a list.
    /// </summary>
    IReadOnlyCollection<ReasonCount> Reasons,
    /// <summary>Every refusal in the range, not only the ones listed below.</summary>
    int Total,
    /// <summary>Payments answered in the range: refused plus confirmed.</summary>
    int Checked,
    /// <summary>The refusals themselves, newest first, up to <see cref="DeclinesReport.Listed"/>.</summary>
    IReadOnlyCollection<DeclinedBooking> Declines)
{
    public const int Listed = 200;
}

public sealed record DeclinesPeriod(
    DateOnly Starts,
    DateOnly Ends,
    int Declined,
    /// <summary>Refused plus confirmed in the period.</summary>
    int Checked,
    /// <summary>Every reason on the list, zero included, in the list's own order; then the uncategorised.</summary>
    IReadOnlyCollection<ReasonCount> Reasons);

public sealed record DeclinedBooking(
    Guid BookingId,
    DateTimeOffset DeclinedAt,
    /// <summary>The day it was refused, on the venue's clock — the day it is counted in.</summary>
    DateOnly DeclinedOn,
    string CustomerName,
    string FacilityName,
    string CourtName,
    string Kind,
    DateOnly StartDate,
    DateOnly EndDate,
    /// <summary>The first hour's start and the last hour's end.</summary>
    TimeOnly? StartsAt,
    TimeOnly? EndsAt,
    int Hours,
    /// <summary>
    /// What the customer sent to pay for it: court rental and the platform's
    /// fee. Null for an attendant whose owner has not shared the money.
    /// </summary>
    decimal? Amount,
    /// <summary>A <c>RejectReason</c>, or null on a refusal from before the list.</summary>
    string? Reason,
    /// <summary>The desk's words: the note, or on an old refusal, all it wrote.</summary>
    string? Note,
    /// <summary>Who turned it down, and whether that was the owner. Null where nobody was recorded.</summary>
    string? DeclinedByName,
    bool DeclinedByOwner);

/// <summary>
/// What customers paid the venue, counted on the day the desk confirmed it.
///
/// Two kinds of money, each on the day it came in: a booking's payment on the
/// day the desk confirmed it, and an upgrade's balance on the day the desk
/// approved it. The platform fee is inside what the customer paid and is the
/// platform's, billed to the venue later; the rest is the venue's takings.
/// </summary>
public sealed record TakingsReport(
    DateOnly From,
    DateOnly To,
    string Grain,
    /// <summary>Every period in the range, zero included: the whole venue's figures.</summary>
    IReadOnlyCollection<TakingsPeriod> Periods,
    /// <summary>
    /// The same money per court per period, only where some came in. A booking
    /// counts to the court it is on now — after an upgrade, the one it moved
    /// to, which is where it is played.
    /// </summary>
    IReadOnlyCollection<CourtTakings> Rows)
{
    /// <summary>
    /// Five years. Wider than the other reports, because takings are what gets
    /// compared year on year, and a sum of payments is cheap to add up.
    /// </summary>
    public const int MostDays = 1827;
}

public sealed record TakingsPeriod(
    DateOnly Starts,
    DateOnly Ends,
    /// <summary>Bookings whose payment was confirmed in the period.</summary>
    int Bookings,
    /// <summary>The hours those bookings are for.</summary>
    int Hours,
    /// <summary>Court rental confirmed: what was paid, less the platform fee.</summary>
    decimal Rental,
    /// <summary>Upgrade balances approved in the period.</summary>
    decimal Upgrades,
    int UpgradeCount,
    /// <summary>The platform's share of what was confirmed. Not the venue's.</summary>
    decimal PlatformFee);

public sealed record CourtTakings(
    DateOnly Starts,
    DateOnly Ends,
    Guid CourtId,
    Guid FacilityId,
    string FacilityName,
    string CourtName,
    int Bookings,
    int Hours,
    decimal Rental,
    decimal Upgrades,
    int UpgradeCount,
    decimal PlatformFee);

/// <summary>
/// What the venue's open, unsold hours would have earned at its own rates.
///
/// Only hours that have begun: an hour still ahead can still be sold, so it
/// is not missed yet. Hours under maintenance, or when the venue was shut or
/// outside its contract, were never on sale and are not counted.
/// </summary>
public sealed record MissedReport(
    DateOnly From,
    DateOnly To,
    string Grain,
    /// <summary>Every period in the range, zero included: the whole venue.</summary>
    IReadOnlyCollection<MissedPeriod> Periods,
    /// <summary>Each court's figures per period, only where it was open.</summary>
    IReadOnlyCollection<CourtMissedPeriod> Rows,
    /// <summary>Each court for the whole range, with the sport courts it is divided into.</summary>
    IReadOnlyCollection<CourtMissed> Courts);

public sealed record MissedPeriod(
    DateOnly Starts,
    DateOnly Ends,
    /// <summary>Minutes the courts were open, up to now.</summary>
    int OpenMinutes,
    /// <summary>Of those, the minutes a court had no booking at all.</summary>
    int NotSoldMinutes,
    int PeakNotSoldMinutes,
    decimal Missed,
    decimal PeakMissed);

public sealed record CourtMissedPeriod(
    DateOnly Starts,
    DateOnly Ends,
    Guid CourtId,
    Guid FacilityId,
    string FacilityName,
    string CourtName,
    int OpenMinutes,
    int NotSoldMinutes,
    decimal Missed);

/// <summary>
/// One court. <see cref="NotSoldMinutes"/> are the minutes the whole floor had
/// no booking. <see cref="Missed"/> prices the court's main sport: every part
/// of it that could still have been sold — all of them when the floor was
/// empty, the rest when some were booked, none when another sport had the
/// floor.
/// </summary>
public sealed record CourtMissed(
    Guid CourtId,
    Guid FacilityId,
    string FacilityName,
    string Name,
    string MainSportName,
    int OpenMinutes,
    int NotSoldMinutes,
    int PeakNotSoldMinutes,
    decimal Missed,
    decimal PeakMissed,
    IReadOnlyCollection<UnitMissed> Units);

/// <summary>
/// One sport court, on its own: the minutes it could have been booked and was
/// not, at its own rate. A minute when a clashing game had the floor is not
/// counted — it could not have been sold. The parts share one floor, so they
/// overlap and do not add up to the court.
/// </summary>
public sealed record UnitMissed(
    Guid BookableCourtId,
    string Label,
    string SportName,
    bool IsMainSport,
    int NotSoldMinutes,
    int PeakNotSoldMinutes,
    decimal Missed);

/// <summary>
/// What the court-mix report is asked for. The range is only for how much each
/// venue type sold; what the venue has is always as it stands now.
/// </summary>
public sealed record CourtMixQuery(
    DateOnly From,
    DateOnly To,
    Guid? FacilityId = null,
    bool IncludeRetired = false);

/// <summary>
/// What a venue has right now: its courts by venue type, and the sports and
/// events each is set up for — with how much of each venue type's open hours
/// sold in the range, from the utilization report's own sums.
///
/// The counts are of courts on sale. Retired courts are listed only when asked
/// for, marked as such, and never counted in the totals: they are not
/// something the venue has any more.
/// </summary>
public sealed record CourtMixReport(
    DateOnly From,
    DateOnly To,
    CourtMixSummary Summary,
    IReadOnlyCollection<VenueTypeMix> VenueTypes,
    IReadOnlyCollection<ActivityMix> Activities,
    IReadOnlyCollection<CourtMixRow> Courts);

public sealed record CourtMixSummary(
    int Courts,
    int BookableCourts,
    /// <summary>Indoor and covered: the courts rain does not stop.</summary>
    int UnderRoof,
    int WithLighting,
    /// <summary>Courts set up for at least one event.</summary>
    int TakeEvents,
    /// <summary>How many different events those courts take.</summary>
    int EventKinds,
    /// <summary>Retired courts at these venues, listed or not.</summary>
    int Retired);

/// <summary>
/// One venue type. <see cref="OpenMinutes"/> and <see cref="InUseMinutes"/>
/// are the utilization report's figures for its courts, added up — so the
/// share here is the same one Court utilisation shows, cut by roof.
/// </summary>
public sealed record VenueTypeMix(
    string VenueType,
    int Courts,
    IReadOnlyCollection<string> CourtNames,
    int OpenMinutes,
    int InUseMinutes);

/// <summary>One sport or event, and how many courts are set up for it.</summary>
public sealed record ActivityMix(
    Guid SportId,
    string Name,
    /// <summary>"Sport" or "Event".</summary>
    string Kind,
    int Courts,
    int BookableCourts,
    /// <summary>On how many of those courts it is the main sport.</summary>
    int MainOn);

public sealed record CourtMixRow(
    Guid CourtId,
    Guid FacilityId,
    string FacilityName,
    string Name,
    string VenueType,
    string? Surface,
    bool HasLighting,
    bool IsRetired,
    int BookableCourts,
    IReadOnlyCollection<CourtActivity> Activities,
    /// <summary>The utilization figures for this court in the range. Zero for a retired court.</summary>
    int OpenMinutes,
    int InUseMinutes);

public sealed record CourtActivity(string Name, string Kind, bool IsMain, int Divisions);

/// <summary>What the court-changes report is asked for. <paramref name="CourtId"/> narrows to one court.</summary>
public sealed record CourtChangesQuery(
    DateOnly From,
    DateOnly To,
    Guid? FacilityId = null,
    Guid? CourtId = null);

/// <summary>
/// Every change made to a venue's courts in a range, newest first, read back
/// out of the audit trail: what it was, what it became, who changed it and why.
/// </summary>
public sealed record CourtChangesReport(
    DateOnly From,
    DateOnly To,
    CourtChangesSummary Summary,
    /// <summary>How many changes of each kind, for the filter chips. Kinds with none are left out.</summary>
    IReadOnlyCollection<ChangeKindCount> Kinds,
    /// <summary>The changes, newest first, up to <see cref="CourtChangesReport.Listed"/>.</summary>
    IReadOnlyCollection<CourtChange> Changes,
    /// <summary>Every change in the range, not only the ones listed.</summary>
    int Total,
    /// <summary>
    /// Every court at the venues in scope, retired ones too, for the court
    /// picker — whichever court the report is narrowed to. A retired court
    /// has a history worth reading.
    /// </summary>
    IReadOnlyCollection<CourtOption> Courts)
{
    public const int Listed = 500;
}

public sealed record CourtOption(Guid Id, Guid FacilityId, string FacilityName, string Name, bool IsActive);

public sealed record CourtChangesSummary(
    /// <summary>Courts on sale now.</summary>
    int Courts,
    int CourtsAdded,
    int CourtsRetired,
    /// <summary>The parts those courts are sold in, now.</summary>
    int BookableCourts,
    int BookableCourtsAdded,
    int BookableCourtsRetired,
    int PriceChanges,
    /// <summary>How many different courts had their prices changed.</summary>
    int CourtsRepriced,
    int Closures,
    /// <summary>Closures for maintenance on at this moment.</summary>
    int ClosedNow);

public sealed record ChangeKindCount(string Kind, int Count);

/// <summary>
/// One change, as a person reads it. The words are made here rather than on
/// the screen, because the audit trail stores ids and the screen does not know
/// which sport an id is.
/// </summary>
public sealed record CourtChange(
    string Id,
    DateTimeOffset At,
    /// <summary>The day and time on the venue's clock.</summary>
    DateOnly On,
    TimeOnly Time,
    /// <summary>One of <see cref="CourtChangeKind"/>.</summary>
    string Kind,
    /// <summary>Null when the change was to the whole venue, such as closing it for maintenance.</summary>
    Guid? CourtId,
    string Title,
    IReadOnlyCollection<ChangeDetail> Details,
    string? Reason,
    string? ActorName,
    /// <summary>"Owner", "Attendant" or "Platform admin".</summary>
    string ActorRole);

/// <summary>One line of a change. <see cref="Before"/> is null for something added, <see cref="After"/> for something removed.</summary>
public sealed record ChangeDetail(string Label, string? Before, string? After);

public static class CourtChangeKind
{
    public const string Added = "Added";
    public const string SportsAndDivisions = "SportsAndDivisions";
    public const string Prices = "Prices";
    public const string Hours = "Hours";
    public const string Maintenance = "Maintenance";
    public const string RenamedOrRetired = "RenamedOrRetired";
    public const string Photos = "Photos";
    public const string Details = "Details";

    public static readonly IReadOnlyCollection<string> All =
        [Added, SportsAndDivisions, Prices, Hours, Maintenance, RenamedOrRetired, Photos, Details];
}

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
