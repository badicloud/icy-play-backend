using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OpenPlayFreePlayerTests
{
    [Fact]
    public void Should_Trim_Name_And_Drop_Blank_Mobile_When_Free_Player_Is_Made()
    {
        // Arrange
        var sessionId = TestIds.For("session");

        // Act
        var player = new OpenPlayFreePlayer(sessionId, "  Coach Ramon ", "   ", TestIds.For("desk"), TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            player.FullName.Should().Be("Coach Ramon");
            player.Mobile.Should().BeNull();
            player.SessionId.Should().Be(sessionId);
            player.IsCheckedIn.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_Refuse_Free_Player_When_Name_Is_Blank(string name)
    {
        // Arrange
        var sessionId = TestIds.For("session");

        // Act
        var act = () => new OpenPlayFreePlayer(sessionId, name, null, TestIds.For("desk"), TestTimes.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Refuse_Free_Player_When_Mobile_Is_Too_Long()
    {
        // Arrange
        var mobile = new string('9', OpenPlayFreePlayer.MobileLimit + 1);

        // Act
        var act = () => new OpenPlayFreePlayer(TestIds.For("session"), "Liza", mobile, TestIds.For("desk"), TestTimes.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Keep_First_Check_In_When_Checked_In_Twice()
    {
        // Arrange
        var player = new OpenPlayFreePlayer(TestIds.For("session"), "Liza", null, TestIds.For("desk"), TestTimes.UtcNow);
        player.CheckIn(TestIds.For("desk"), TestTimes.UtcNow);

        // Act
        player.CheckIn(TestIds.For("other-desk"), TestTimes.UtcNow.AddMinutes(5));

        // Assert
        using (new AssertionScope())
        {
            player.CheckedInAt.Should().Be(TestTimes.UtcNow);
            player.CheckedInByUserId.Should().Be(TestIds.For("desk"));
        }
    }

    [Fact]
    public void Should_Clear_Check_In_When_It_Is_Undone()
    {
        // Arrange
        var player = new OpenPlayFreePlayer(TestIds.For("session"), "Liza", null, TestIds.For("desk"), TestTimes.UtcNow);
        player.CheckIn(TestIds.For("desk"), TestTimes.UtcNow);

        // Act
        player.UndoCheckIn(TestTimes.UtcNow.AddMinutes(1));

        // Assert
        using (new AssertionScope())
        {
            player.IsCheckedIn.Should().BeFalse();
            player.CheckedInByUserId.Should().BeNull();
        }
    }
}
