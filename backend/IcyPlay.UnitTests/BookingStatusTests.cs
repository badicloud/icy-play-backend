using IcyPlay.Domain.Bookings;

namespace IcyPlay.UnitTests;

public sealed class BookingStatusTests
{
    [Fact]
    public void ConfirmedStatus_ShouldKeepExpectedValue()
    {
        ((int)BookingStatus.Confirmed).Should().Be(3);
    }
}
