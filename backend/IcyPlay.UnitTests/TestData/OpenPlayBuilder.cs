using IcyPlay.Domain.OpenPlays;

namespace IcyPlay.UnitTests.TestData;

/// <summary>
/// A valid open play by default: Saturdays, six to nine in the evening, from
/// Saturday 17 January 2026 with no end date, ₱150 a head, closing an hour
/// before the start, and no early bird.
/// </summary>
public sealed class OpenPlayBuilder
{
    public static readonly DateOnly FirstSaturday = new(2026, 1, 17);

    private string _title = "Saturday Night Dinkers";
    private string _level = OpenPlayLevel.AllLevels;
    private int _maxPlayers = 16;
    private decimal _registrationFee = 150m;
    private TimeOnly _startsAt = new(18, 0);
    private TimeOnly _endsAt = new(21, 0);
    private OpenPlayDays _days = OpenPlayDays.Saturday;
    private DateOnly _startDate = FirstSaturday;
    private DateOnly? _endDate;
    private int _cutoffMinutes = 60;
    private OpenPlayEarlyBird? _earlyBird;

    public OpenPlayBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public OpenPlayBuilder WithLevel(string level)
    {
        _level = level;
        return this;
    }

    public OpenPlayBuilder WithMaxPlayers(int maxPlayers)
    {
        _maxPlayers = maxPlayers;
        return this;
    }

    public OpenPlayBuilder WithRegistrationFee(decimal fee)
    {
        _registrationFee = fee;
        return this;
    }

    public OpenPlayBuilder WithHours(TimeOnly startsAt, TimeOnly endsAt)
    {
        _startsAt = startsAt;
        _endsAt = endsAt;
        return this;
    }

    public OpenPlayBuilder OnDays(OpenPlayDays days)
    {
        _days = days;
        return this;
    }

    public OpenPlayBuilder Running(DateOnly startDate, DateOnly? endDate)
    {
        _startDate = startDate;
        _endDate = endDate;
        return this;
    }

    public OpenPlayBuilder WithCutoffMinutes(int minutes)
    {
        _cutoffMinutes = minutes;
        return this;
    }

    public OpenPlayBuilder WithEarlyBird(string kind, decimal value, int leadMinutes)
    {
        _earlyBird = new OpenPlayEarlyBird(kind, value, leadMinutes);
        return this;
    }

    public OpenPlay Build() =>
        new(
            TestIds.FacilityId,
            TestIds.For("bookable-court"),
            TestIds.CourtId,
            _title,
            _level,
            _maxPlayers,
            _registrationFee,
            _startsAt,
            _endsAt,
            _days,
            _startDate,
            _endDate,
            _cutoffMinutes,
            _earlyBird,
            TestIds.FacilityOwnerUserId,
            TestTimes.UtcNow);
}
