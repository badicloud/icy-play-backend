using FluentAssertions.Execution;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Payments;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class ContractPaymentTermsTests
{
    [Fact]
    public void Should_Default_To_Manual_With_Fifteen_Minute_Online_Hold_When_Term_Is_Commenced()
    {
        // Arrange
        // Act
        var contract = Contract();

        // Assert
        using (new AssertionScope())
        {
            contract.PaymentMode.Should().Be(PaymentMode.Manual);
            contract.OnlineHoldMinutes.Should().Be(15);
            contract.TakesDirectPayment.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Take_Direct_Payment_When_Admin_Sets_Direct_Mode()
    {
        // Arrange
        var contract = Contract();

        // Act
        contract.SetPaymentTerms(PaymentMode.Direct, 20, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            contract.TakesDirectPayment.Should().BeTrue();
            contract.OnlineHoldMinutes.Should().Be(20);
        }
    }

    [Fact]
    public void Should_Clamp_Online_Hold_When_It_Is_Below_The_Floor()
    {
        // Arrange
        var contract = Contract();

        // Act
        contract.SetPaymentTerms(PaymentMode.Direct, 1, TestTimes.UtcNow);

        // Assert
        contract.OnlineHoldMinutes.Should().Be(5);
    }

    [Fact]
    public void Should_Throw_When_Payment_Mode_Is_Not_Manual_Or_Direct()
    {
        // Arrange
        var contract = Contract();

        // Act
        var act = () => contract.SetPaymentTerms("Both", 15, TestTimes.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static FacilityOwnerContract Contract() =>
        new(
            TestIds.For("facility-owner"),
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31),
            TestIds.For("admin"),
            notes: null,
            TestTimes.UtcNow);
}
