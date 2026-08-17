using FluentAssertions.Execution;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class UserTests
{
    [Fact]
    public void Should_Normalize_Email_When_Email_Has_Whitespace_And_Mixed_Case()
    {
        // Arrange
        var userBuilder = new UserBuilder()
            .WithEmail("  Juan@Example.COM ");

        // Act
        var user = userBuilder.Build();

        // Assert
        user.Email.Should().Be("juan@example.com");
    }

    [Fact]
    public void Should_Lock_User_When_Maximum_Failed_Login_Attempts_Are_Reached()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var now = TestTimes.UtcNow;

        // Act
        for (var attempt = 0; attempt < 5; attempt++)
        {
            user.RecordFailedLogin(5, TimeSpan.FromMinutes(15), now);
        }

        // Assert
        using (new AssertionScope())
        {
            user.FailedLoginAttempts.Should().Be(5);
            user.LockoutEnd.Should().Be(now.AddMinutes(15));
        }
    }

    [Fact]
    public void Should_Reset_Lockout_State_When_Successful_Login_Is_Recorded_For_Locked_User()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var now = TestTimes.UtcNow;
        user.RecordFailedLogin(1, TimeSpan.FromMinutes(15), now);

        // Act
        user.RecordSuccessfulLogin(now.AddMinutes(1));

        // Assert
        using (new AssertionScope())
        {
            user.FailedLoginAttempts.Should().Be(0);
            user.LockoutEnd.Should().BeNull();
        }
    }

}
