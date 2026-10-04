using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OpenPlayCheckInCodeTests
{
    private const string Code = "482913";

    [Theory]
    [InlineData("482913", true)]
    [InlineData("48291", false)]
    [InlineData("4829130", false)]
    [InlineData("48a913", false)]
    [InlineData(" 48291", false)]
    [InlineData(null, false)]
    public void Should_Take_Only_Six_Digits_When_A_Code_Is_Checked(string? code, bool expected)
    {
        // Arrange
        // Act
        var wellFormed = CheckInCode.IsWellFormed(code);

        // Assert
        wellFormed.Should().Be(expected);
    }

    [Fact]
    public void Should_Make_Six_Digits_That_Are_Not_Obvious_When_A_Code_Is_Generated()
    {
        // Arrange
        // Act
        var codes = Enumerable.Range(0, 200).Select(_ => CheckInCode.Generate()).ToArray();

        // Assert
        using (new AssertionScope())
        {
            codes.Should().OnlyContain(code => CheckInCode.IsWellFormed(code));
            codes.Should().NotContain(code => CheckInCode.IsObvious(code));
            codes.Distinct().Should().HaveCountGreaterThan(1);
        }
    }

    [Theory]
    [InlineData("000000", true)]
    [InlineData("777777", true)]
    [InlineData("123456", true)]
    [InlineData("987654", true)]
    [InlineData("482913", false)]
    [InlineData("135791", false)]
    public void Should_Call_A_Code_Obvious_When_It_Is_One_Digit_Or_A_Straight_Run(string code, bool expected)
    {
        // Arrange
        // Act
        var obvious = CheckInCode.IsObvious(code);

        // Assert
        obvious.Should().Be(expected);
    }

    [Fact]
    public void Should_Keep_Only_A_Salted_Hash_When_The_Code_Is_Set()
    {
        // Arrange
        // Act
        var first = CheckInCode.Hash(Code);
        var second = CheckInCode.Hash(Code);

        // Assert
        using (new AssertionScope())
        {
            first.Should().NotContain(Code);
            first.Should().NotBe(second);
            CheckInCode.Matches(Code, first).Should().BeTrue();
            CheckInCode.Matches("482914", first).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Refuse_Every_Code_When_The_Venue_Has_Not_Set_One()
    {
        // Arrange
        var owner = new FacilityOwnerBuilder().Build();

        // Act
        var verdict = owner.TryOpenPlayCheckInCode(Code, TestTimes.UtcNow);

        // Assert
        verdict.Should().Be(CheckInCodeVerdict.NotSet);
    }

    [Fact]
    public void Should_Accept_The_Code_When_It_Is_Right()
    {
        // Arrange
        var owner = new FacilityOwnerBuilder().WithCheckInCode(Code).Build();

        // Act
        var verdict = owner.TryOpenPlayCheckInCode(Code, TestTimes.UtcNow);

        // Assert
        verdict.Should().Be(CheckInCodeVerdict.Accepted);
    }

    [Fact]
    public void Should_Lock_The_Code_When_It_Is_Wrong_Five_Times()
    {
        // Arrange
        var owner = new FacilityOwnerBuilder().WithCheckInCode(Code).Build();

        for (var attempt = 0; attempt < CheckInCode.MaxFailures - 1; attempt++)
        {
            owner.TryOpenPlayCheckInCode("000000", TestTimes.UtcNow);
        }

        // Act
        var fifth = owner.TryOpenPlayCheckInCode("000000", TestTimes.UtcNow);
        var rightButLocked = owner.TryOpenPlayCheckInCode(Code, TestTimes.UtcNow.AddMinutes(1));

        // Assert
        using (new AssertionScope())
        {
            fifth.Should().Be(CheckInCodeVerdict.Locked);
            rightButLocked.Should().Be(CheckInCodeVerdict.Locked);
            owner.CheckInCodeLockedUntil.Should().Be(TestTimes.UtcNow.AddMinutes(CheckInCode.LockMinutes));
        }
    }

    [Fact]
    public void Should_Accept_The_Code_Again_When_The_Lock_Has_Run_Out()
    {
        // Arrange
        var owner = new FacilityOwnerBuilder().WithCheckInCode(Code).Build();

        for (var attempt = 0; attempt < CheckInCode.MaxFailures; attempt++)
        {
            owner.TryOpenPlayCheckInCode("000000", TestTimes.UtcNow);
        }

        // Act
        var verdict = owner.TryOpenPlayCheckInCode(Code, TestTimes.UtcNow.AddMinutes(CheckInCode.LockMinutes));

        // Assert
        verdict.Should().Be(CheckInCodeVerdict.Accepted);
    }

    [Fact]
    public void Should_Start_The_Count_Again_When_A_Right_Code_Follows_Wrong_Ones()
    {
        // Arrange
        var owner = new FacilityOwnerBuilder().WithCheckInCode(Code).Build();
        owner.TryOpenPlayCheckInCode("000000", TestTimes.UtcNow);
        owner.TryOpenPlayCheckInCode("000000", TestTimes.UtcNow);

        // Act
        owner.TryOpenPlayCheckInCode(Code, TestTimes.UtcNow);

        // Assert
        owner.CheckInCodeFailures.Should().Be(0);
    }
}
