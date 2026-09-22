using FluentAssertions.Execution;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

/// <summary>
/// Which hours of a booking count as already played, and so stay where they
/// were when the rest of it moves.
///
/// Its own file because this one predicate is read from two sides that have to
/// agree: the customer's upgrade records the hours it means by it, and the
/// venue's desk checks that record against the same question later. They were
/// once written out separately and did not agree — one asked whether an hour
/// had finished, the other whether it had begun — and every approval made while
/// an hour was running was refused as no longer adding up.
/// </summary>
public sealed class BookingHoursPlayedTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    [Fact]
    public void Should_Count_An_Hour_As_Played_Once_It_Has_Begun()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .Build();

        var venueNow = Today.ToDateTime(new TimeOnly(14, 10));

        // Act
        var played = booking.Slots.Where(slot => slot.HasBegunAt(venueNow)).ToArray();

        // Assert
        using (new AssertionScope())
        {
            // 1-2 has finished; 2-3 is half spent on the court they are
            // standing on. Both stay.
            played.Should().HaveCount(2);
            played.Select(slot => slot.StartsAt)
                .Should()
                .BeEquivalentTo([new TimeOnly(13, 0), new TimeOnly(14, 0)]);
        }
    }

    [Fact]
    public void Should_Leave_Whole_Hours_Still_Ahead_To_Be_Moved()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .Build();

        var venueNow = Today.ToDateTime(new TimeOnly(14, 10));

        // Act
        var toMove = booking.Slots.Where(slot => !slot.HasBegunAt(venueNow)).ToArray();

        // Assert
        using (new AssertionScope())
        {
            toMove.Should().ContainSingle();
            toMove[0].StartsAt.Should().Be(new TimeOnly(15, 0));
        }
    }

    /// <summary>
    /// The partition the desk relies on: what was recorded as moving, plus what
    /// has been played, has to still account for the whole booking.
    /// </summary>
    [Fact]
    public void Should_Still_Account_For_Every_Hour_When_The_Desk_Asks_Again_Within_The_Same_Hour()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(4)
            .Build();

        var whenAsked = Today.ToDateTime(new TimeOnly(15, 8));
        var whenApproved = Today.ToDateTime(new TimeOnly(15, 40));

        // Act
        var moving = booking.Slots.Count(slot => !slot.HasBegunAt(whenAsked));
        var playedByThen = booking.Slots.Count(slot => slot.HasBegunAt(whenApproved));

        // Assert
        using (new AssertionScope())
        {
            moving.Should().Be(1);
            (playedByThen + moving).Should().Be(booking.Slots.Count);
        }
    }

    /// <summary>
    /// And still goes stale when it should: once the hour being paid for has
    /// begun on the old court, the sums really have stopped adding up.
    /// </summary>
    [Fact]
    public void Should_Stop_Adding_Up_Once_The_Hour_Being_Paid_For_Has_Itself_Begun()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(4)
            .Build();

        var whenAsked = Today.ToDateTime(new TimeOnly(15, 8));
        var whenApproved = Today.ToDateTime(new TimeOnly(16, 5));

        // Act
        var moving = booking.Slots.Count(slot => !slot.HasBegunAt(whenAsked));
        var playedByThen = booking.Slots.Count(slot => slot.HasBegunAt(whenApproved));

        // Assert
        (playedByThen + moving).Should().NotBe(booking.Slots.Count);
    }

    [Fact]
    public void Should_Count_Nothing_As_Played_On_A_Booking_That_Has_Not_Started()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(18, 0))
            .LastingHours(3)
            .Build();

        var venueNow = Today.ToDateTime(new TimeOnly(14, 0));

        // Act
        var played = booking.Slots.Where(slot => slot.HasBegunAt(venueNow)).ToArray();

        // Assert
        played.Should().BeEmpty();
    }

    [Fact]
    public void Should_Count_An_Hour_As_Played_At_The_Exact_Moment_It_Opens()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(1)
            .Build();

        // Act
        var played = booking.Slots.Single().HasBegunAt(Today.ToDateTime(new TimeOnly(13, 0)));

        // Assert
        played.Should().BeTrue();
    }
}
