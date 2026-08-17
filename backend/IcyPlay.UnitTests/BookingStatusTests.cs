using IcyPlay.Domain.Bookings;

namespace IcyPlay.UnitTests;

public sealed class BookingStatusTests
{
    [Fact]
    public void Should_Return_Stable_Persistence_Value_When_Confirmed_Status_Is_Converted_To_Integer()
    {
        // Arrange
        const int expectedValue = 3;

        // Act
        var actualValue = (int)BookingStatus.Confirmed;

        // Assert
        actualValue.Should().Be(expectedValue);
    }
}
