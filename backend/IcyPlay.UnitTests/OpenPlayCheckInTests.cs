using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OpenPlayCheckInTests
{
    private const string Receipt = "https://res.cloudinary.com/icyplay-test/image/upload/v1/receipt.jpg";
    private const string Token = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";

    private static readonly DateOnly Saturday = OpenPlayBuilder.FirstSaturday;

    [Fact]
    public void Should_Open_Check_In_An_Hour_Before_When_The_Window_Is_Left_Alone()
    {
        // Arrange: six to nine.
        var openPlay = new OpenPlayBuilder().WithHours(new(18, 0), new(21, 0)).Build();

        // Act
        var before = openPlay.IsCheckInOpen(Saturday, Saturday.ToDateTime(new(16, 59)));
        var opens = openPlay.IsCheckInOpen(Saturday, Saturday.ToDateTime(new(17, 0)));
        var during = openPlay.IsCheckInOpen(Saturday, Saturday.ToDateTime(new(20, 30)));
        var over = openPlay.IsCheckInOpen(Saturday, Saturday.ToDateTime(new(21, 0)));

        // Assert: from an hour before until the session ends.
        using (new AssertionScope())
        {
            before.Should().BeFalse();
            opens.Should().BeTrue();
            during.Should().BeTrue();
            over.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Move_The_Window_When_It_Is_Set_On_A_Published_Open_Play()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().WithHours(new(18, 0), new(21, 0)).Build();
        openPlay.Publish(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act: how the venue runs its door, so allowed after publishing.
        openPlay.SetCheckInWindow(30, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            openPlay.CheckInOpensAt(Saturday).Should().Be(Saturday.ToDateTime(new(17, 30)));
            openPlay.IsCheckInOpen(Saturday, Saturday.ToDateTime(new(17, 15))).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Keep_Check_In_Shut_When_The_Date_Is_Not_A_Session()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        var sunday = Saturday.AddDays(1);

        // Act
        var open = openPlay.IsCheckInOpen(sunday, sunday.ToDateTime(new(18, 30)));

        // Assert
        open.Should().BeFalse();
    }

    [Fact]
    public void Should_Refuse_A_Window_When_It_Is_Longer_Than_A_Day()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();

        // Act
        var set = () => openPlay.SetCheckInWindow(25 * 60, TestTimes.UtcNow);

        // Assert
        set.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_Check_In_A_Registered_Player_When_They_Arrive()
    {
        // Arrange
        var registration = Registered();

        // Act
        registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            registration.IsCheckedIn.Should().BeTrue();
            registration.CheckedInByUserId.Should().Be(TestIds.FacilityOwnerUserId);
        }
    }

    [Fact]
    public void Should_Refuse_A_Second_Check_In_When_The_Player_Is_Already_In()
    {
        // Arrange
        var registration = Registered();
        registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act
        var again = () => registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Take_The_Check_In_Back_When_It_Is_Undone()
    {
        // Arrange
        var registration = Registered();
        registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act
        registration.UndoCheckIn(TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            registration.IsCheckedIn.Should().BeFalse();
            registration.CheckedInByUserId.Should().BeNull();
        }
    }

    [Fact]
    public void Should_Refuse_Check_In_When_The_Payment_Is_Not_Confirmed()
    {
        // Arrange: a receipt sent, the desk not looked yet.
        var registration = Waiting();

        // Act
        var checkIn = () => registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        checkIn.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Read_The_Token_When_The_Scan_Is_One_Of_Our_Qrs()
    {
        // Arrange
        var scanned = CheckInPass.QrContent(Token);

        // Act
        var token = CheckInPass.TokenFrom(scanned);

        // Assert
        token.Should().Be(Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/menu")]
    [InlineData("icyplay-checkin:")]
    public void Should_Read_No_Token_When_The_Scan_Is_Not_One_Of_Our_Qrs(string? scanned)
    {
        // Arrange
        // Act
        var token = CheckInPass.TokenFrom(scanned);

        // Assert
        token.Should().BeNull();
    }

    [Fact]
    public void Should_Make_No_Qr_When_The_Payment_Is_Not_Confirmed()
    {
        // Arrange
        var registration = Waiting();

        // Act
        var state = registration.PassState(sessionHasEnded: false);

        // Assert
        using (new AssertionScope())
        {
            registration.CheckInToken.Should().BeNull();
            state.Should().BeNull();
        }
    }

    [Fact]
    public void Should_Give_The_Registration_Its_Own_Qr_When_The_Payment_Is_Confirmed()
    {
        // Arrange
        var first = Registered();

        // Act
        var second = Registered();

        // Assert: long, random, and one per registration.
        using (new AssertionScope())
        {
            first.CheckInToken.Should().HaveLength(43);
            first.CheckInToken.Should().NotBe(second.CheckInToken);
            first.PassState(sessionHasEnded: false).Should().Be(CheckInPassState.Active);
        }
    }

    [Fact]
    public void Should_Spend_The_Qr_When_The_Player_Is_Checked_In()
    {
        // Arrange
        var registration = Registered();

        // Act
        registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        registration.PassState(sessionHasEnded: false).Should().Be(CheckInPassState.Used);
    }

    [Fact]
    public void Should_Make_The_Qr_Good_Again_When_The_Check_In_Is_Undone()
    {
        // Arrange
        var registration = Registered();
        registration.CheckIn(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act
        registration.UndoCheckIn(TestTimes.UtcNow);

        // Assert
        registration.PassState(sessionHasEnded: false).Should().Be(CheckInPassState.Active);
    }

    [Fact]
    public void Should_Expire_The_Qr_When_The_Session_Ends_Without_A_Check_In()
    {
        // Arrange
        var registration = Registered();

        // Act
        var state = registration.PassState(sessionHasEnded: true);

        // Assert
        state.Should().Be(CheckInPassState.Expired);
    }

    private static OpenPlayRegistration Waiting()
    {
        var registration = new OpenPlayRegistration(
            TestIds.For("session"), TestIds.CustomerUserId, new OpenPlayPrice(150m, 0m, 15m), 30,
            TestTimes.UtcNow, TestTimes.UtcNow);
        registration.SendReceipt(Receipt, TestTimes.UtcNow);

        return registration;
    }

    private static OpenPlayRegistration Registered()
    {
        var registration = Waiting();
        registration.Confirm(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        return registration;
    }
}
