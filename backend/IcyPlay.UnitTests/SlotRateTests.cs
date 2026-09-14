using FluentAssertions.Execution;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.UnitTests;

/// <summary>
/// What one hour costs. Four rates and a peak window can all apply to the same
/// Saturday evening, and the order they are applied in is money.
/// </summary>
public sealed class SlotRateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    // 14 Sep 2026 is a Monday; 19 Sep is the Saturday.
    private static readonly DateOnly Weekday = new(2026, 9, 14);
    private static readonly DateOnly Weekend = new(2026, 9, 19);

    private static readonly TimeOnly Morning = new(9, 0);
    private static readonly TimeOnly Evening = new(18, 0);

    [Fact]
    public void Should_Charge_The_Standard_Rate_On_An_Ordinary_Weekday_Morning()
    {
        // Arrange
        var sut = Priced(standard: 500m, peak: 600m, weekend: 550m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(Court(), Weekday, Morning, isHoliday: false);

        // Assert
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Standard);
            rate.Amount.Should().Be(500m);
        }
    }

    [Fact]
    public void Should_Charge_The_Peak_Rate_Inside_The_Window()
    {
        // Arrange
        var sut = Priced(standard: 500m, peak: 600m, weekend: 550m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(Court(), Weekday, Evening, isHoliday: false);

        // Assert
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Peak);
            rate.Amount.Should().Be(600m);
        }
    }

    [Fact]
    public void Should_Let_Peak_Beat_The_Weekend_Rate_When_Both_Apply()
    {
        // Arrange: Saturday evening is a weekend AND inside the peak window,
        // and the venue ticked the box saying peak runs at weekends.
        var sut = Priced(standard: 500m, peak: 600m, weekend: 550m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(Court(), Weekend, Evening, isHoliday: false);

        // Assert: the busiest hour of the busiest day charges the higher of the
        // two the venue configured.
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Peak);
            rate.Amount.Should().Be(600m);
        }
    }

    [Fact]
    public void Should_Keep_The_Day_Rate_When_It_Is_Already_Above_Peak()
    {
        // Arrange: this venue prices its whole weekend above its peak rate.
        var sut = Priced(standard: 500m, peak: 600m, weekend: 700m, holiday: 900m);

        // Act
        var rate = sut.PriceAt(Court(), Weekend, Evening, isHoliday: false);

        // Assert: peak lifts an ordinary day, it does not cap a dearer one.
        // Charging 600 here would undercut a price the venue set deliberately.
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Weekend);
            rate.Amount.Should().Be(700m);
        }
    }

    [Fact]
    public void Should_Still_Charge_Peak_On_A_Weekend_The_Venue_Discounted()
    {
        // Arrange: the weekend rate is BELOW standard -- a venue trying to fill
        // quiet Saturdays -- while peak is above it.
        var sut = Priced(standard: 500m, peak: 600m, weekend: 400m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(Court(), Weekend, Evening, isHoliday: false);

        // Assert: the discount does NOT survive the peak window, because peak is
        // one absolute number for every day it applies to. Pinned because it is
        // a real limitation rather than an accident -- a venue that wants cheap
        // weekend evenings cannot say so today.
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Peak);
            rate.Amount.Should().Be(600m);
        }
    }

    [Fact]
    public void Should_Charge_The_Holiday_Rate_Over_The_Weekend_One()
    {
        // Arrange: a holiday that happens to fall on a Saturday.
        var sut = Priced(standard: 500m, peak: 600m, weekend: 550m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(Court(), Weekend, Morning, isHoliday: true);

        // Assert: the more specific day wins. A named date beats "it is a
        // Saturday".
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Holiday);
            rate.Amount.Should().Be(700m);
        }
    }

    [Fact]
    public void Should_Keep_The_Holiday_Rate_When_It_Beats_Peak()
    {
        // Arrange: a holiday evening, where the holiday rate is the higher of
        // the two.
        var sut = Priced(standard: 500m, peak: 600m, weekend: 550m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(Court(), Weekday, Evening, isHoliday: true);

        // Assert
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Holiday);
            rate.Amount.Should().Be(700m);
        }
    }

    [Fact]
    public void Should_Fall_Back_To_Standard_For_A_Rate_The_Venue_Left_Blank()
    {
        // Arrange: only a standard rate is set, which is the ordinary case.
        var sut = Priced(standard: 500m, peak: null, weekend: null, holiday: null);

        // Act
        var evening = sut.PriceAt(Court(), Weekend, Evening, isHoliday: true);

        // Assert: a blank special rate means "same as standard", not "free".
        evening!.Amount.Should().Be(500m);
    }

    [Fact]
    public void Should_Have_No_Price_At_All_When_The_Sport_Is_Unpriced()
    {
        // Arrange
        var sut = new CourtSport(Guid.NewGuid(), Guid.NewGuid(), true, 1, Now);

        // Act, Assert: an unpriced sport is not sellable, and answering zero
        // would put it on sale for nothing.
        sut.PriceAt(Court(), Weekday, Morning, isHoliday: false).Should().BeNull();
    }

    [Fact]
    public void Should_Ignore_The_Peak_Window_On_Days_It_Does_Not_Apply_To()
    {
        // Arrange: peak runs on weekdays only.
        var court = Court(onWeekdays: true, onWeekends: false);
        var sut = Priced(standard: 500m, peak: 600m, weekend: 550m, holiday: 700m);

        // Act
        var rate = sut.PriceAt(court, Weekend, Evening, isHoliday: false);

        // Assert
        using (new AssertionScope())
        {
            rate!.Kind.Should().Be(CourtRateKind.Weekend);
            rate.Amount.Should().Be(550m);
        }
    }

    private static CourtSport Priced(
        decimal standard,
        decimal? peak,
        decimal? weekend,
        decimal? holiday)
    {
        var pair = new CourtSport(Guid.NewGuid(), Guid.NewGuid(), true, 1, Now);
        pair.SetPricing(standard, peak, weekend, holiday, Now);

        return pair;
    }

    /// <summary>A court whose peak window runs 5 to 8 in the evening.</summary>
    private static Court Court(bool onWeekdays = true, bool onWeekends = true)
    {
        var court = new Court(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Court 1",
            10,
            null,
            new CourtSpace(CourtVenueType.Covered, CourtSurface.Concrete, true, null, null, null),
            new CourtBookingRules(60, 60, 0),
            Now);
        court.SetPeakWindow(new TimeOnly(17, 0), new TimeOnly(20, 0), onWeekdays, onWeekends, Now);

        return court;
    }
}
