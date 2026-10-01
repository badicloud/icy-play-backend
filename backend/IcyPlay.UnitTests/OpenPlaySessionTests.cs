using FluentAssertions.Execution;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OpenPlaySessionTests
{
    private const string Receipt = "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg";

    private static readonly OpenPlayPrice Price = new(150m, 0m, 15m);

    [Fact]
    public void Should_Count_A_Registration_When_It_Is_Inside_Its_Payment_Hold()
    {
        // Arrange
        var session = NewSession();
        session.Registrations.Add(Register(1));

        // Act
        var left = session.SpotsLeftAt(TestTimes.UtcNow.AddMinutes(10), 4);

        // Assert
        left.Should().Be(3);
    }

    [Fact]
    public void Should_Free_The_Spot_When_The_Hold_Lapses_Without_A_Receipt()
    {
        // Arrange
        var session = NewSession();
        session.Registrations.Add(Register(1));

        // Act
        var left = session.SpotsLeftAt(TestTimes.UtcNow.AddHours(2), 4);

        // Assert
        left.Should().Be(4);
    }

    [Fact]
    public void Should_Keep_The_Spot_When_A_Receipt_Was_Sent_Before_The_Hold_Lapsed()
    {
        // Arrange
        var session = NewSession();
        var registration = Register(1);
        registration.SendReceipt(Receipt, TestTimes.UtcNow.AddMinutes(5));
        session.Registrations.Add(registration);

        // Act
        var left = session.SpotsLeftAt(TestTimes.UtcNow.AddHours(2), 4);

        // Assert
        left.Should().Be(3);
    }

    [Fact]
    public void Should_Cancel_Every_Registration_When_The_Session_Is_Cancelled()
    {
        // Arrange
        var session = NewSession();
        var confirmed = Register(1);
        confirmed.SendReceipt(Receipt, TestTimes.UtcNow);
        confirmed.Confirm(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);
        session.Registrations.Add(confirmed);
        session.Registrations.Add(Register(2));

        // Act
        session.Cancel("Not enough players", TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            session.IsCancelled.Should().BeTrue();
            session.Registrations.Should().OnlyContain(r => r.Status == BookingStatus.Cancelled);
            session.SpotsTakenAt(TestTimes.UtcNow).Should().Be(0);
        }
    }

    [Fact]
    public void Should_Refuse_To_Cancel_When_No_Reason_Is_Given()
    {
        // Arrange
        var session = NewSession();

        // Act
        var cancel = () => session.Cancel(" ", TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        cancel.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Keep_The_Price_Snapshot_When_The_Registration_Is_Created()
    {
        // Arrange
        var price = new OpenPlayPrice(150m, 50m, 15m);

        // Act
        var registration = new OpenPlayRegistration(
            TestIds.For("session"), TestIds.CustomerUserId, price, 30, TestTimes.UtcNow, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            registration.Discount.Should().Be(50m);
            registration.PlatformFee.Should().Be(15m);
            registration.Total.Should().Be(115m);
            registration.AgreedToPolicyAt.Should().Be(TestTimes.UtcNow);
        }
    }

    private static OpenPlaySession NewSession() =>
        new(TestIds.For("open-play"), OpenPlayBuilder.FirstSaturday, TestTimes.UtcNow);

    private static OpenPlayRegistration Register(int player) =>
        new(TestIds.For("session"), TestIds.For("player", player), Price, 30, TestTimes.UtcNow, TestTimes.UtcNow);
}
