using FluentAssertions.Execution;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OpenPlayRegistrationTests
{
    private const string Receipt = "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg";

    [Fact]
    public void Should_Hand_It_To_The_Desk_When_The_First_Receipt_Is_Sent()
    {
        // Arrange
        var registration = NewRegistration();

        // Act
        var first = registration.SendReceipt(Receipt, TestTimes.UtcNow.AddMinutes(5));

        // Assert
        using (new AssertionScope())
        {
            first.Should().BeTrue();
            registration.Status.Should().Be(BookingStatus.PendingVerification);
            registration.SubmittedForVerificationAt.Should().Be(TestTimes.UtcNow.AddMinutes(5));
            registration.IsRegistered.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Only_Replace_The_Picture_When_A_Second_Receipt_Is_Sent()
    {
        // Arrange
        var registration = NewRegistration();
        registration.SendReceipt(Receipt, TestTimes.UtcNow.AddMinutes(5));

        // Act
        var first = registration.SendReceipt(Receipt.Replace("receipt", "better"), TestTimes.UtcNow.AddMinutes(9));

        // Assert
        using (new AssertionScope())
        {
            first.Should().BeFalse();
            registration.SubmittedForVerificationAt.Should().Be(TestTimes.UtcNow.AddMinutes(5));
            registration.ReceiptUrl.Should().Contain("better");
        }
    }

    [Fact]
    public void Should_Refuse_A_Receipt_When_The_Hold_Has_Run_Out()
    {
        // Arrange
        var registration = NewRegistration();

        // Act
        var send = () => registration.SendReceipt(Receipt, TestTimes.UtcNow.AddHours(2));

        // Assert
        send.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Register_The_Player_When_The_Desk_Confirms()
    {
        // Arrange
        var registration = NewRegistration();
        registration.SendReceipt(Receipt, TestTimes.UtcNow);

        // Act
        registration.Confirm(TestIds.FacilityOwnerUserId, TestTimes.UtcNow.AddMinutes(20));

        // Assert
        using (new AssertionScope())
        {
            registration.IsRegistered.Should().BeTrue();
            registration.ConfirmedByUserId.Should().Be(TestIds.FacilityOwnerUserId);
        }
    }

    [Fact]
    public void Should_Refuse_To_Confirm_When_No_Receipt_Has_Been_Sent()
    {
        // Arrange
        var registration = NewRegistration();

        // Act
        var confirm = () => registration.Confirm(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        confirm.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Release_The_Spot_And_Say_Why_When_The_Desk_Rejects()
    {
        // Arrange
        var registration = NewRegistration();
        registration.SendReceipt(Receipt, TestTimes.UtcNow);

        // Act
        registration.Reject(RejectReason.WrongAmount, "Sent 100, owed 165", TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            registration.Status.Should().Be(BookingStatus.Rejected);
            registration.HoldsSpotAt(TestTimes.UtcNow).Should().BeFalse();
            registration.CancellationReason.Should().Be("Wrong amount — Sent 100, owed 165");
            registration.RejectedByUserId.Should().Be(TestIds.FacilityOwnerUserId);
        }
    }

    [Fact]
    public void Should_Refuse_A_Reason_When_It_Is_Not_On_The_Desks_List()
    {
        // Arrange
        var registration = NewRegistration();
        registration.SendReceipt(Receipt, TestTimes.UtcNow);

        // Act
        var reject = () => registration.Reject("Whatever", null, TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        reject.Should().Throw<ArgumentException>();
    }

    private static OpenPlayRegistration NewRegistration() =>
        new(
            TestIds.For("session"),
            TestIds.CustomerUserId,
            new OpenPlayPrice(150m, 0m, 15m),
            30,
            TestTimes.UtcNow,
            TestTimes.UtcNow);
}
