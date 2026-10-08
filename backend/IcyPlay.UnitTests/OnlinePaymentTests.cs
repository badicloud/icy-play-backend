using FluentAssertions.Execution;
using IcyPlay.Domain.Payments;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OnlinePaymentTests
{
    [Fact]
    public void Should_Cover_What_Is_Due_When_Net_Amount_Equals_Venue_Share_And_Platform_Fee()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);

        // Act
        payment.RecordPaid("pay_1", "qrph", 639.60m, 9.60m, 630m, TestTimes.UtcNow, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            payment.AmountDue.Should().Be(630m);
            payment.CoversWhatIsDue.Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Not_Cover_What_Is_Due_When_Net_Amount_Is_Short_By_A_Centavo()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);

        // Act
        payment.RecordPaid("pay_1", "gcash", 644.36m, 14.37m, 629.99m, TestTimes.UtcNow, TestTimes.UtcNow);

        // Assert
        payment.CoversWhatIsDue.Should().BeFalse();
    }

    [Fact]
    public void Should_Not_Cover_What_Is_Due_When_Nothing_Has_Been_Paid()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);

        // Act
        var covered = payment.CoversWhatIsDue;

        // Assert
        covered.Should().BeFalse();
    }

    [Fact]
    public void Should_Keep_Reason_When_Paid_Payment_Is_Flagged_For_Attention()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);

        // Act
        payment.FlagForAttention(AttentionReason.PaidAfterHoldLapsed, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            payment.Status.Should().Be(OnlinePaymentStatus.NeedsAttention);
            payment.AttentionReason.Should().Be(AttentionReason.PaidAfterHoldLapsed);
            payment.IsPending.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Throw_When_An_Amount_Owed_Is_Negative()
    {
        // Arrange
        // Act
        var act = () => Payment(venueAmount: -1m, platformFee: 30m);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private static OnlinePayment Payment(decimal venueAmount, decimal platformFee) =>
        new(
            PaymentPurpose.Booking,
            TestIds.For("booking"),
            TestIds.For("facility-owner"),
            TestIds.CustomerUserId,
            venueAmount,
            platformFee,
            "PayMongo",
            TestTimes.UtcNow);
}
