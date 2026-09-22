using FluentAssertions.Execution;
using IcyPlay.Domain.Bookings;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

/// <summary>
/// The rule behind "Happening now" on a customer's list of bookings.
///
/// Worth its own file because the answer changes with the clock, and every way
/// of getting it slightly wrong sends somebody to a court: too generous and a
/// booking that finished hours ago still says the court is theirs, too narrow
/// and the badge never appears at all.
/// </summary>
public sealed class BookingInProgressTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    [Fact]
    public void Should_Be_Playing_When_The_Venue_Clock_Is_Inside_A_Booked_Hour()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(14, 30)));

        // Assert
        playing.Should().BeTrue();
    }

    [Fact]
    public void Should_Not_Be_Playing_When_The_Booking_Has_Not_Started()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(12, 59)));

        // Assert
        playing.Should().BeFalse();
    }

    /// <summary>
    /// The difference between this and "has started", which stays true for ever
    /// once the first hour has gone by.
    /// </summary>
    [Fact]
    public void Should_Not_Be_Playing_When_The_Last_Hour_Has_Finished()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(16, 0)));

        // Assert
        using (new AssertionScope())
        {
            playing.Should().BeFalse();
            booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(23, 0))).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Be_Playing_At_The_Exact_Moment_The_First_Hour_Opens()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(1)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(13, 0)));

        // Assert
        playing.Should().BeTrue();
    }

    /// <summary>
    /// The end is exclusive, or two back-to-back hours both claim the instant
    /// between them.
    /// </summary>
    [Fact]
    public void Should_Not_Be_Playing_At_The_Exact_Moment_The_Last_Hour_Closes()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(1)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(14, 0)));

        // Assert
        playing.Should().BeFalse();
    }

    /// <summary>
    /// Hourly bookings need not run back to back, so the gap between two bought
    /// hours is not bought and the customer is not on court in it.
    /// </summary>
    [Fact]
    public void Should_Not_Be_Playing_In_The_Gap_Between_Two_Hours_That_Are_Not_Back_To_Back()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .WithHours(new TimeOnly(13, 0), new TimeOnly(16, 0))
            .Build();

        // Act
        var playingInTheGap = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(15, 0)));

        // Assert
        using (new AssertionScope())
        {
            playingInTheGap.Should().BeFalse();
            booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(13, 30))).Should().BeTrue();
            booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(16, 30))).Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Not_Be_Playing_On_Another_Date_At_The_Same_Time_Of_Day()
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.AddDays(1).ToDateTime(new TimeOnly(14, 0)));

        // Assert
        playing.Should().BeFalse();
    }

    [Theory]
    [InlineData(BookingStatus.PendingPayment)]
    [InlineData(BookingStatus.PendingVerification)]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Cancelled)]
    public void Should_Not_Be_Playing_When_The_Venue_Has_Not_Confirmed_It(BookingStatus status)
    {
        // Arrange
        var booking = new BookingBuilder()
            .On(Today)
            .StartingAt(new TimeOnly(13, 0))
            .LastingHours(3)
            .WithStatus(status)
            .Build();

        // Act
        var playing = booking.IsPlayingAt(Today.ToDateTime(new TimeOnly(14, 30)));

        // Assert
        playing.Should().BeFalse();
    }
}
