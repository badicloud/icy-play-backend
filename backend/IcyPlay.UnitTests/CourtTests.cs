using FluentAssertions.Execution;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.UnitTests;

public sealed class CourtTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, "Court 1")]
    [InlineData(3, "Court 1 \u00b7 Pickleball 2")]
    public void Should_Number_A_Division_Only_When_The_Court_Is_Actually_Divided(
        int divisions,
        string expected)
    {
        // Act
        var name = Court.DivisionName("Court 1", "Pickleball", 2, divisions);

        // Assert: a court played whole keeps its own name. Numbering one of one
        // only invites the question of where the second one is.
        name.Should().Be(expected);
    }

    [Fact]
    public void Should_Follow_The_Facility_Hours_Until_Told_Otherwise()
    {
        // Act
        var sut = CreateCourt();

        // Assert: an owner with eight courts types the schedule once.
        using (new AssertionScope())
        {
            sut.UsesFacilityHours.Should().BeTrue();
            sut.IsActive.Should().BeTrue();
        }
    }

    [Theory]
    // Shorter than one slot: the calendar cannot offer it.
    [InlineData(60, 30)]
    // Not a whole number of slots, so it would end mid-slot.
    [InlineData(60, 90)]
    [InlineData(30, 45)]
    public void Should_Reject_A_Minimum_That_Is_Not_A_Whole_Number_Of_Slots(
        int slotLength,
        int minimumDuration)
    {
        // Act
        var act = () => CreateCourt(rules: new CourtBookingRules(slotLength, minimumDuration, 0));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(60, 60)]
    [InlineData(60, 120)]
    [InlineData(30, 90)]
    public void Should_Accept_A_Minimum_That_Divides_Into_Slots(int slotLength, int minimumDuration)
    {
        // Act
        var sut = CreateCourt(rules: new CourtBookingRules(slotLength, minimumDuration, 0));

        // Assert
        using (new AssertionScope())
        {
            sut.SlotLengthMinutes.Should().Be(slotLength);
            sut.MinimumDurationMinutes.Should().Be(minimumDuration);
        }
    }

    [Fact]
    public void Should_Reject_A_Slot_Of_No_Length()
    {
        // Act
        var act = () => CreateCourt(rules: new CourtBookingRules(0, 60, 0));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_A_Negative_Buffer()
    {
        // Act
        var act = () => CreateCourt(rules: new CourtBookingRules(60, 60, -15));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_A_Venue_Type_That_Is_Not_One_Of_The_Three()
    {
        // Act
        var act = () => CreateCourt(venueType: "Rooftop");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Treat_A_Capacity_Of_Zero_As_Not_Recorded()
    {
        // Act
        var sut = CreateCourt(capacity: 0);

        // Assert: zero people is not a capacity, it is a blank field.
        sut.Capacity.Should().BeNull();
    }

    private static Court CreateCourt(
        string venueType = CourtVenueType.Covered,
        int? capacity = 12,
        CourtBookingRules? rules = null) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Court 1",
        10,
        "The near court.",
        new CourtSpace(venueType, CourtSurface.Concrete, true, "Full court", capacity, "Net provided"),
        rules ?? new CourtBookingRules(60, 60, 0),
        Now);
}

public sealed class MaintenancePeriodTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_Cover_A_Moment_Inside_An_Open_Ended_Closure()
    {
        // Arrange: closed until further notice.
        var sut = CreatePeriod(endsAt: null);

        // Assert
        using (new AssertionScope())
        {
            sut.Covers(Now).Should().BeTrue();
            sut.Covers(Now.AddYears(1)).Should().BeTrue();
            sut.Covers(Now.AddMinutes(-1)).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Stop_Covering_Once_The_Closure_Is_Lifted()
    {
        // Arrange
        var sut = CreatePeriod(endsAt: null);

        // Act
        sut.Lift(Now.AddHours(2));

        // Assert: the record stays, the closure does not.
        using (new AssertionScope())
        {
            sut.Covers(Now.AddHours(3)).Should().BeFalse();
            sut.LiftedAt.Should().Be(Now.AddHours(2));
        }
    }

    [Fact]
    public void Should_Apply_To_Every_Court_When_No_Court_Is_Named()
    {
        // Act
        var sut = CreatePeriod(courtId: null);

        // Assert
        sut.AppliesToWholeFacility.Should().BeTrue();
    }

    [Fact]
    public void Should_Reject_A_Closure_That_Ends_Before_It_Starts()
    {
        // Act
        var act = () => CreatePeriod(endsAt: Now.AddHours(-1));

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_A_Closure_With_No_Reason()
    {
        // Act: the reason is what the affected customers are told.
        var act = () => new MaintenancePeriod(
            Guid.NewGuid(),
            null,
            Now,
            null,
            "   ",
            Guid.NewGuid(),
            Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static MaintenancePeriod CreatePeriod(
        Guid? courtId = null,
        DateTimeOffset? endsAt = null) => new(
        Guid.NewGuid(),
        courtId,
        Now,
        endsAt,
        "Resurfacing the floor",
        Guid.NewGuid(),
        Now);
}
