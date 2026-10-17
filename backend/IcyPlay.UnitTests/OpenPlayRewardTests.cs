using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

/// <summary>What an open play's best players earn: a voucher, or a prize against a claim stub.</summary>
public sealed class OpenPlayRewardTests
{
    [Theory]
    [InlineData("Percentage", 101, null, 30)]
    [InlineData("Fixed", 0, null, 30)]
    [InlineData("Fixed", 50, 20.0, 30)]
    [InlineData("Percentage", 10, 0.0, 30)]
    [InlineData("Fixed", 50, null, 0)]
    [InlineData("Fixed", 50, null, 366)]
    [InlineData("Free", 50, null, 30)]
    public void Should_Refuse_A_Voucher_When_It_Does_Not_Make_Sense(string kind, double value, double? cap, int days)
    {
        // Act
        var make = () => OpenPlayReward.Voucher(kind, (decimal)value, (decimal?)cap, days);

        // Assert
        make.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", 5)]
    [InlineData("Free Gatorade", 0)]
    [InlineData("Free Gatorade", 501)]
    public void Should_Refuse_A_Claim_Stub_Prize_When_It_Does_Not_Make_Sense(string prize, int quantity)
    {
        // Act
        var make = () => OpenPlayReward.ClaimStub(prize, quantity, 30);

        // Assert
        make.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Take_No_More_Than_The_Fee_When_A_Fixed_Voucher_Is_Worth_More()
    {
        // Arrange
        var voucher = Voucher(OpenPlayReward.Voucher("Fixed", 150, null, 30));

        // Act
        var off = voucher.DiscountOn(100);

        // Assert
        off.Should().Be(100);
    }

    [Fact]
    public void Should_Stop_At_The_Most_Off_When_A_Percentage_Voucher_Has_One()
    {
        // Arrange: 50% of 300 is 150, but no more than 100.
        var voucher = Voucher(OpenPlayReward.Voucher("Percentage", 50, 100, 30));

        // Act
        var off = voucher.DiscountOn(300);

        // Assert
        off.Should().Be(100);
    }

    [Fact]
    public void Should_Expire_A_Voucher_When_Its_Days_Are_Up()
    {
        // Arrange
        var voucher = Voucher(OpenPlayReward.Voucher("Fixed", 50, null, 30));

        // Act
        var dayBefore = voucher.IsUsableAt(TestTimes.UtcNow.AddDays(29));
        var dayAfter = voucher.IsUsableAt(TestTimes.UtcNow.AddDays(30));

        // Assert
        using (new AssertionScope())
        {
            voucher.ExpiresAt.Should().Be(TestTimes.UtcNow.AddDays(30));
            dayBefore.Should().BeTrue();
            dayAfter.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Go_Back_To_The_Prizes_To_Give_When_A_Given_Stub_Is_Taken_Back()
    {
        // Arrange
        var stub = new OpenPlayClaimStub(TestIds.For("open-play"), TestIds.FacilityId, "7KQ2-M9XA", "Free Gatorade", TestTimes.UtcNow);
        stub.Assign(TestIds.For("player"), TestIds.For("session"), 7, TestTimes.UtcNow);

        // Act
        var given = stub.StatusAt(TestTimes.UtcNow.AddDays(1));
        var expired = stub.StatusAt(TestTimes.UtcNow.AddDays(7));
        var takenBack = stub.Unassign(TestTimes.UtcNow.AddDays(1));

        // Assert
        using (new AssertionScope())
        {
            given.Should().Be(OpenPlayClaimStubStatus.Given);
            expired.Should().Be(OpenPlayClaimStubStatus.Expired);
            takenBack.Should().BeTrue();
            stub.StatusAt(TestTimes.UtcNow.AddDays(1)).Should().Be(OpenPlayClaimStubStatus.Available);
        }
    }

    [Fact]
    public void Should_Make_A_Code_Without_Look_Alike_Characters_When_A_Stub_Is_Made()
    {
        // Act
        var codes = Enumerable.Range(0, 200).Select(_ => OpenPlayClaimStub.NewCode()).ToList();

        // Assert
        using (new AssertionScope())
        {
            codes.Should().OnlyContain(code => code.Length == 9 && code[4] == '-');
            codes.Should().OnlyContain(code => !code.Any(letter => "01OIL".Contains(letter)));
        }
    }

    [Fact]
    public void Should_Take_The_Voucher_Off_After_The_Early_Bird_When_It_Is_Applied()
    {
        // Arrange: ₱200 fee, ₱20 early bird, ₱15 platform fee, a ₱50 voucher.
        var registration = Registration(new OpenPlayPrice(200, 20, 15));
        var voucher = Voucher(OpenPlayReward.Voucher("Fixed", 50, null, 30));

        // Act
        registration.ApplyVoucher(voucher, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            registration.VoucherDiscount.Should().Be(50);
            registration.VenueAmount.Should().Be(130);
            registration.Total.Should().Be(145);
            voucher.Status.Should().Be(OpenPlayVoucherStatus.Reserved);
            voucher.RegistrationId.Should().Be(registration.Id);
        }
    }

    [Fact]
    public void Should_Leave_Only_The_Platform_Fee_When_A_Voucher_Covers_The_Venues_Whole_Fee()
    {
        // Arrange
        var registration = Registration(new OpenPlayPrice(100, 0, 15));
        var voucher = Voucher(OpenPlayReward.Voucher("Fixed", 500, null, 30));

        // Act
        registration.ApplyVoucher(voucher, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            registration.VenueAmount.Should().Be(0);
            registration.Total.Should().Be(15);
        }
    }

    [Fact]
    public void Should_Be_The_Players_Again_When_The_Registration_It_Was_Held_For_Falls_Through()
    {
        // Arrange
        var registration = Registration(new OpenPlayPrice(200, 0, 15));
        var voucher = Voucher(OpenPlayReward.Voucher("Fixed", 50, null, 30));
        registration.ApplyVoucher(voucher, TestTimes.UtcNow);

        // Act
        voucher.Release(TestTimes.UtcNow.AddHours(1));

        // Assert
        using (new AssertionScope())
        {
            voucher.Status.Should().Be(OpenPlayVoucherStatus.Available);
            voucher.RegistrationId.Should().BeNull();
            voucher.IsUsableAt(TestTimes.UtcNow.AddHours(1)).Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Refuse_A_Second_Voucher_When_One_Is_Already_On_The_Registration()
    {
        // Arrange
        var registration = Registration(new OpenPlayPrice(200, 0, 15));
        registration.ApplyVoucher(Voucher(OpenPlayReward.Voucher("Fixed", 50, null, 30)), TestTimes.UtcNow);

        // Act
        var again = () => registration.ApplyVoucher(Voucher(OpenPlayReward.Voucher("Fixed", 20, null, 30)), TestTimes.UtcNow);

        // Assert
        again.Should().Throw<InvalidOperationException>();
    }

    private static OpenPlayRegistration Registration(OpenPlayPrice price) =>
        new(TestIds.For("session"), TestIds.For("player"), price, 15, TestTimes.UtcNow, TestTimes.UtcNow);

    private static OpenPlayVoucher Voucher(OpenPlayReward reward) =>
        new(TestIds.FacilityId, TestIds.For("player"), TestIds.For("open-play"), TestIds.For("session"), reward, TestTimes.UtcNow);
}
