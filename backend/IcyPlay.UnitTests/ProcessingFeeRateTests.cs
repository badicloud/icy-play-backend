using FluentAssertions.Execution;
using IcyPlay.Domain.Payments;

namespace IcyPlay.UnitTests;

public sealed class ProcessingFeeRateTests
{
    [Theory]
    [InlineData(1.34, 0, 630, 639.61)]
    [InlineData(2.23, 0, 630, 646.14)]
    [InlineData(1.79, 0, 630, 642.90)]
    [InlineData(3.125, 13.39, 630, 668.40)]
    [InlineData(2.23, 0, 15.50, 15.91)]
    public void Should_Charge_Smallest_Total_That_Leaves_Amount_Due_When_Fee_Is_Taken(
        decimal percent,
        decimal fixedAmount,
        decimal amountDue,
        decimal expectedTotal)
    {
        // Arrange
        var rate = new ProcessingFeeRate(percent, fixedAmount);

        // Act
        var total = rate.GrossUp(amountDue);

        // Assert
        using (new AssertionScope())
        {
            total.Should().Be(expectedTotal);
            (total - rate.FeeOn(total)).Should().BeGreaterThanOrEqualTo(amountDue);
            (total - 0.01m - rate.FeeOn(total - 0.01m)).Should().BeLessThan(amountDue);
        }
    }

    [Fact]
    public void Should_Include_Vat_When_Working_Out_Fee()
    {
        // Arrange
        var rate = new ProcessingFeeRate(2.23m);

        // Act
        var fee = rate.FeeOn(646.15m);

        // Assert
        fee.Should().Be(16.14m);
    }

    [Fact]
    public void Should_Round_Each_Part_Of_Fee_Up_When_It_Falls_Between_Centavos()
    {
        // Arrange
        var rate = new ProcessingFeeRate(1.34m);

        // Act
        var fee = rate.FeeOn(639.60m);

        // Assert
        fee.Should().Be(9.61m);
    }

    [Fact]
    public void Should_Leave_At_Least_Amount_Due_When_Amounts_Across_A_Wide_Range_Are_Grossed_Up()
    {
        // Arrange
        var rate = new ProcessingFeeRate(1.34m);
        var leftShort = new List<decimal>();

        // Act
        for (var amountDue = 1m; amountDue <= 2000m; amountDue += 0.37m)
        {
            var total = rate.GrossUp(amountDue);

            if (total - rate.FeeOn(total) < amountDue)
            {
                leftShort.Add(amountDue);
            }
        }

        // Assert
        leftShort.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Should_Refuse_To_Gross_Up_When_Nothing_Is_Owed(decimal amountDue)
    {
        // Arrange
        var rate = new ProcessingFeeRate(1.34m);

        // Act
        var grossUp = () => rate.GrossUp(amountDue);

        // Assert
        grossUp.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(50, 0)]
    [InlineData(1.34, -1)]
    public void Should_Refuse_Rate_When_It_Is_Out_Of_Range(decimal percent, decimal fixedAmount)
    {
        // Arrange
        var published = (Percent: percent, Fixed: fixedAmount);

        // Act
        var create = () => new ProcessingFeeRate(published.Percent, published.Fixed);

        // Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
