using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One sport a court accommodates. Its own record rather than a tag, because
/// pricing will hang off the pair: a court that takes badminton and basketball
/// will carry a price for each.
/// </summary>
public sealed class CourtSport : Entity
{
    private CourtSport()
    {
    }

    public CourtSport(
        Guid courtId,
        Guid sportId,
        bool isPrimary,
        int divisions,
        DateTimeOffset createdAt)
    {
        CourtId = courtId;
        SportId = sportId;
        IsPrimary = isPrimary;
        Divisions = divisions < 1 ? 1 : divisions;
        CreatedAt = createdAt;
    }

    public Guid CourtId
    {
        get; private set;
    }
    public Court Court { get; private set; } = null!;
    public Guid SportId
    {
        get; private set;
    }
    public Sport Sport { get; private set; } = null!;

    /// <summary>What the court is listed as when one name has to be shown.</summary>
    public bool IsPrimary
    {
        get; private set;
    }

    /// <summary>
    /// How many playable courts this one makes when it is used for this sport.
    /// A full basketball court is three pickleball courts across, and each of
    /// those is booked and paid for on its own.
    ///
    /// One means the court is played whole, which is the ordinary case.
    /// </summary>
    public int Divisions { get; private set; } = 1;

    public bool IsDivided => Divisions > 1;

    /// <summary>
    /// What this pair actually sells: one row per playable part, kept in step
    /// with <see cref="Divisions"/> by the roster. Retired parts stay in the
    /// collection, so read it filtered on <c>IsActive</c>.
    /// </summary>
    public ICollection<BookableCourt> BookableCourts { get; private set; } = [];

    public void SetPrimary(bool isPrimary, DateTimeOffset now)
    {
        IsPrimary = isPrimary;
        UpdatedAt = now;
    }

    public void SetDivisions(int divisions, DateTimeOffset now)
    {
        Divisions = divisions < 1 ? 1 : divisions;
        UpdatedAt = now;
    }

    /// <summary>
    /// What an hour of this sport costs on this court. The three special rates
    /// are optional and fall back to the standard one, so a venue that charges
    /// the same all week stores one number rather than four copies of it.
    /// </summary>
    public decimal? StandardHourlyRate
    {
        get; private set;
    }
    public decimal? PeakHourlyRate
    {
        get; private set;
    }
    public decimal? WeekendRate
    {
        get; private set;
    }
    public decimal? HolidayRate
    {
        get; private set;
    }

    /// <summary>A sport with no standard rate has no price, so it cannot be sold.</summary>
    public bool IsPriced => StandardHourlyRate is not null;

    /// <summary>
    /// The rate actually charged for a kind of hour. Asking the pair rather
    /// than reading four columns at the call site is what keeps the fallback
    /// from being spelled differently in each place that needs it.
    /// </summary>
    public decimal? RateFor(CourtRateKind kind) => kind switch
    {
        CourtRateKind.Peak => PeakHourlyRate ?? StandardHourlyRate,
        CourtRateKind.Weekend => WeekendRate ?? StandardHourlyRate,
        CourtRateKind.Holiday => HolidayRate ?? StandardHourlyRate,
        _ => StandardHourlyRate
    };

    /// <summary>
    /// What one hour costs, and under which rate.
    ///
    /// The day decides the base -- a holiday, then a weekend, then an ordinary
    /// day -- and the peak window lifts it, but never lowers it. A venue that
    /// prices its whole weekend above peak keeps the weekend rate through the
    /// window; one that leaves the weekend ordinary gets the peak premium on its
    /// busiest hours.
    ///
    /// What this CANNOT express: a cheaper weekend evening. Peak is one absolute
    /// number for every day it applies to, so a venue that discounts weekends
    /// still charges peak inside the window. Expressing that would need a peak
    /// rate per day type, which nothing has asked for yet.
    ///
    /// Null when the sport has no standard rate, which is the one thing that
    /// makes it unsellable.
    /// </summary>
    public SlotRate? PriceAt(Court court, DateOnly date, TimeOnly startsAt, bool isHoliday)
    {
        ArgumentNullException.ThrowIfNull(court);

        if (StandardHourlyRate is null)
        {
            return null;
        }

        var dayKind = isHoliday
            ? CourtRateKind.Holiday
            : Court.IsWeekend(date.DayOfWeek)
                ? CourtRateKind.Weekend
                : CourtRateKind.Standard;

        var dayRate = RateFor(dayKind)!.Value;

        if (!court.IsPeakAt(date.DayOfWeek, startsAt))
        {
            return new SlotRate(dayKind, dayRate);
        }

        var peakRate = RateFor(CourtRateKind.Peak)!.Value;

        return peakRate > dayRate
            ? new SlotRate(CourtRateKind.Peak, peakRate)
            : new SlotRate(dayKind, dayRate);
    }

    public void SetPricing(
        decimal? standardHourlyRate,
        decimal? peakHourlyRate,
        decimal? weekendRate,
        decimal? holidayRate,
        DateTimeOffset now)
    {
        StandardHourlyRate = standardHourlyRate;
        PeakHourlyRate = peakHourlyRate;
        WeekendRate = weekendRate;
        HolidayRate = holidayRate;
        UpdatedAt = now;
    }
}

/// <summary>
/// One hour, priced: what it costs and why. Both halves travel together because
/// a customer shown "600" without "peak" beside it reads it as a mistake.
/// </summary>
public sealed record SlotRate(CourtRateKind Kind, decimal Amount);

/// <summary>Which of a court's rates an hour falls under.</summary>
public enum CourtRateKind
{
    Standard,
    Peak,
    Weekend,
    Holiday
}
