using FluentAssertions.Execution;
using IcyPlay.Domain.Bookings;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class BookingOnlinePaymentTests
{
    [Fact]
    public void Should_Confirm_And_Record_Total_Paid_When_Direct_Booking_Is_Paid_Within_Hold()
    {
        // Arrange
        var booking = new BookingBuilder()
            .PaidDirect()
            .WithStatus(BookingStatus.PendingPayment)
            .LastingHours(2)
            .Build();
        var paidAt = TestTimes.UtcNow.AddMinutes(14);
        var now = TestTimes.UtcNow.AddMinutes(16);

        // Act
        booking.ConfirmPaidOnline(paidAt, now);

        // Assert
        using (new AssertionScope())
        {
            booking.Status.Should().Be(BookingStatus.Confirmed);
            booking.ConfirmedAt.Should().Be(now);
            booking.PaidTotal.Should().Be(630m);
            booking.ReceiptUrl.Should().BeNull();
        }
    }

    [Fact]
    public void Should_Not_Be_Confirmable_Online_When_Paid_After_Hold_Lapsed()
    {
        // Arrange
        var booking = new BookingBuilder()
            .PaidDirect(holdMinutes: 15)
            .WithStatus(BookingStatus.PendingPayment)
            .Build();

        // Act
        var confirmable = booking.CanBeConfirmedOnlineBy(TestTimes.UtcNow.AddMinutes(15));

        // Assert
        confirmable.Should().BeFalse();
    }

    [Fact]
    public void Should_Throw_When_Direct_Booking_Is_Confirmed_After_Hold_Lapsed()
    {
        // Arrange
        var booking = new BookingBuilder()
            .PaidDirect(holdMinutes: 15)
            .WithStatus(BookingStatus.PendingPayment)
            .Build();

        // Act
        var act = () => booking.ConfirmPaidOnline(TestTimes.UtcNow.AddMinutes(20), TestTimes.UtcNow.AddMinutes(20));

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Throw_When_Receipt_Booking_Is_Confirmed_Online()
    {
        // Arrange
        var booking = new BookingBuilder()
            .WithStatus(BookingStatus.PendingPayment)
            .Build();

        // Act
        var act = () => booking.ConfirmPaidOnline(TestTimes.UtcNow, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            act.Should().Throw<InvalidOperationException>();
            booking.CanBeConfirmedOnlineBy(TestTimes.UtcNow).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Not_Be_Confirmable_Online_When_Booking_Is_Cancelled()
    {
        // Arrange
        var booking = new BookingBuilder()
            .PaidDirect()
            .WithStatus(BookingStatus.Cancelled)
            .Build();

        // Act
        var confirmable = booking.CanBeConfirmedOnlineBy(TestTimes.UtcNow.AddMinutes(1));

        // Assert
        confirmable.Should().BeFalse();
    }

    [Fact]
    public void Should_Default_To_Manual_Channel_When_No_Channel_Is_Given()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var paidDirect = booking.IsPaidDirect;

        // Assert
        paidDirect.Should().BeFalse();
    }
}
