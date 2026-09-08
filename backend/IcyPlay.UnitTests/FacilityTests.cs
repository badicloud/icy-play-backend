using FluentAssertions.Execution;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.UnitTests;

public sealed class FacilityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Abc Sports Center", "abc-sports-center")]
    [InlineData("  Spaced   Out  ", "spaced-out")]
    [InlineData("Parañaque Courts", "paranaque-courts")]
    [InlineData("Court #1 & #2!", "court-1-2")]
    [InlineData("---", "")]
    public void Should_Build_A_Url_Safe_Slug_From_The_Facility_Name(string name, string expected)
    {
        // Act
        var slug = Facility.ToSlug(name);

        // Assert
        slug.Should().Be(expected);
    }

    [Fact]
    public void Should_Keep_Both_Coordinates_When_A_Valid_Pin_Is_Given()
    {
        // Arrange
        var sut = CreateFacility();

        // Act
        sut.SetCoordinates(7.073056m, 125.612222m, Now);

        // Assert
        using (new AssertionScope())
        {
            sut.Latitude.Should().Be(7.073056m);
            sut.Longitude.Should().Be(125.612222m);
            sut.HasCoordinates.Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Clear_Both_Coordinates_When_Only_One_Is_Given()
    {
        // Arrange
        var sut = CreateFacility();
        sut.SetCoordinates(7.073056m, 125.612222m, Now);

        // Act: half a coordinate points nowhere, so it must not survive.
        sut.SetCoordinates(7.073056m, null, Now);

        // Assert
        using (new AssertionScope())
        {
            sut.Latitude.Should().BeNull();
            sut.Longitude.Should().BeNull();
            sut.HasCoordinates.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(91, 120)]
    [InlineData(-91, 120)]
    [InlineData(10, 181)]
    [InlineData(10, -181)]
    public void Should_Reject_A_Coordinate_Outside_The_Range_Of_The_Globe(decimal latitude, decimal longitude)
    {
        // Arrange
        var sut = CreateFacility();

        // Act
        var act = () => sut.SetCoordinates(latitude, longitude, Now);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_Default_To_Manila_When_No_Time_Zone_Is_Given()
    {
        // Act
        var sut = CreateFacility(timeZone: "   ");

        // Assert
        sut.TimeZone.Should().Be(Facility.DefaultTimeZone);
    }

    private static Facility CreateFacility(string timeZone = "Asia/Manila") => new(
        Guid.NewGuid(),
        "Abc Sports Center",
        "abc-sports-center",
        "Six covered courts.",
        new FacilityAddress("123 Quimpo Boulevard", null, "Davao City", "Davao del Sur", "8000", "Philippines"),
        new FacilityContact("+639171234567", "hello@abc.example"),
        new FacilityPolicies("First aid kit on site.", "No street shoes on the court."),
        timeZone,
        Now);
}

public sealed class FacilityOperatingHourTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_Report_Closed_When_Neither_Time_Is_Given()
    {
        // Act
        var sut = new FacilityOperatingHour(Guid.NewGuid(), DayOfWeek.Sunday, null, null, Now);

        // Assert
        sut.IsClosed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Should_Reject_A_Day_That_Has_Only_One_Of_The_Two_Times(bool hasOpening, bool hasClosing)
    {
        // Arrange
        var opensAt = hasOpening ? new TimeOnly(6, 0) : (TimeOnly?)null;
        var closesAt = hasClosing ? new TimeOnly(22, 0) : (TimeOnly?)null;

        // Act
        var act = () => new FacilityOperatingHour(Guid.NewGuid(), DayOfWeek.Monday, opensAt, closesAt, Now);

        // Assert: closed is the absence of both, not a half-filled row.
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_A_Closing_Time_That_Is_Not_After_The_Opening_Time()
    {
        // Act
        var act = () => new FacilityOperatingHour(
            Guid.NewGuid(),
            DayOfWeek.Monday,
            new TimeOnly(22, 0),
            new TimeOnly(6, 0),
            Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Accept_A_Normal_Trading_Day()
    {
        // Act
        var sut = new FacilityOperatingHour(
            Guid.NewGuid(),
            DayOfWeek.Monday,
            new TimeOnly(6, 0),
            new TimeOnly(22, 0),
            Now);

        // Assert
        using (new AssertionScope())
        {
            sut.IsClosed.Should().BeFalse();
            sut.OpensAt.Should().Be(new TimeOnly(6, 0));
            sut.ClosesAt.Should().Be(new TimeOnly(22, 0));
        }
    }
}
