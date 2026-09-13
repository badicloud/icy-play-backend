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
/// Which of a court's rates an hour falls under. The windows that decide
/// between them -- when peak runs, which dates are holidays -- are not modelled
/// yet, so only Standard is reachable in practice today.
/// </summary>
public enum CourtRateKind
{
    Standard,
    Peak,
    Weekend,
    Holiday
}
