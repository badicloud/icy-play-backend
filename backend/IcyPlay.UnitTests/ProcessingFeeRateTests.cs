using FluentAssertions.Execution;
using IcyPlay.Domain.Payments;

namespace IcyPlay.UnitTests;

public sealed class ProcessingFeeRateTests
{
    [Theory]
    [InlineData(2.5, 515, 528.21)]
    [InlineData(2.0, 515, 525.52)]
    [InlineData(1.5, 515, 522.85)]
    [InlineData(2.5, 630, 646.16)]
    [InlineData(1.5, 630, 639.60)]
    [InlineData(2.5, 15.50, 15.90)]
    public void Should_Charge_Smallest_Total_That_Leaves_Amount_Due_When_Fee_Is_Taken(
        decimal percent,
        decimal amountDue,
        decimal expectedTotal)
    {
        // Arrange
        var rate = new ProcessingFeeRate(percent);

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

    /// <summary>
    /// Regression: a GCash payment of ₱528.20 for ₱515 was charged a ₱13.21 fee
    /// by PayMongo and arrived ₱514.99 — a centavo short, flagged for the desk,
    /// and paid a second time by the customer. VAT had been worked out apart
    /// from the rate (2.4976%) where PayMongo takes a round 2.5%.
    /// </summary>
    [Fact]
    public void Should_Leave_Amount_Due_When_GCash_Total_That_Came_Up_Short_Is_Worked_Out_Again()
    {
        // Arrange
        var gcash = new ProcessingFeeRate(2.5m);

        // Act
        var total = gcash.GrossUp(515m);

        // Assert
        using (new AssertionScope())
        {
            total.Should().Be(528.21m);
            (total - PayMongoFee(total, 2.5m)).Should().Be(515m);
        }
    }

    /// <summary>
    /// What PayMongo took on real test payments, read back from the database:
    /// the rate VAT included, times what was charged, to the nearest centavo.
    /// Ours never comes out under theirs, and never more than a centavo over.
    /// </summary>
    [Theory]
    [InlineData(2.5, 646.15, 16.15)]
    [InlineData(2.5, 528.20, 13.21)]
    [InlineData(2.5, 102.56, 2.56)]
    [InlineData(2.5, 117.96, 2.95)]
    [InlineData(2.0, 525.54, 10.51)]
    [InlineData(1.5, 421.33, 6.32)]
    [InlineData(1.5, 319.81, 4.80)]
    [InlineData(1.5, 1263.96, 18.96)]
    public void Should_Never_Work_Out_Less_Than_PayMongo_Took_When_Fee_Is_Worked_Out(
        decimal percent,
        decimal charged,
        decimal paymongoFee)
    {
        // Arrange
        var rate = new ProcessingFeeRate(percent);

        // Act
        var fee = rate.FeeOn(charged);

        // Assert
        using (new AssertionScope())
        {
            PayMongoFee(charged, percent).Should().Be(paymongoFee);
            fee.Should().BeInRange(paymongoFee, paymongoFee + 0.01m);
        }
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(2.5)]
    public void Should_Leave_At_Least_Amount_Due_When_PayMongo_Takes_Its_Fee_Across_A_Wide_Range(decimal percent)
    {
        // Arrange
        var rate = new ProcessingFeeRate(percent);
        var leftShort = new List<decimal>();

        // Act
        for (var amountDue = 1m; amountDue <= 5000m; amountDue += 0.37m)
        {
            var total = rate.GrossUp(amountDue);

            if (total - PayMongoFee(total, percent) < amountDue)
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
        var rate = new ProcessingFeeRate(2.5m);

        // Act
        var grossUp = () => rate.GrossUp(amountDue);

        // Assert
        grossUp.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(50, 0)]
    [InlineData(1.5, -1)]
    public void Should_Refuse_Rate_When_It_Is_Out_Of_Range(decimal percent, decimal fixedAmount)
    {
        // Arrange
        var published = (Percent: percent, Fixed: fixedAmount);

        // Act
        var create = () => new ProcessingFeeRate(published.Percent, published.Fixed);

        // Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>PayMongo's own sum, as its payments show it: to the nearest centavo, halves up.</summary>
    private static decimal PayMongoFee(decimal charged, decimal percent) =>
        Math.Round(charged * percent / 100m, 2, MidpointRounding.AwayFromZero);
}
