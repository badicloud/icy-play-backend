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

    [Fact]
    public void Should_Keep_Method_Total_And_Qr_Code_When_Intent_Is_Opened()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);
        var expiresAt = TestTimes.UtcNow.AddMinutes(10);

        // Act
        payment.OpenIntent("pi_1", "qrph", 639.61m, "data:image/png;base64,QR", null, expiresAt, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            payment.PaymentIntentId.Should().Be("pi_1");
            payment.ChosenMethod.Should().Be("qrph");
            payment.QuotedTotal.Should().Be(639.61m);
            payment.QrCodeImage.Should().Be("data:image/png;base64,QR");
            payment.ExpiresAt.Should().Be(expiresAt);
            payment.CheckoutUrl.Should().BeNull();
            payment.IsPending.Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Throw_When_Intent_Total_Is_Less_Than_What_Is_Owed()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);

        // Act
        var act = () => payment.OpenIntent("pi_1", "gcash", 629.99m, null, "https://gcash.test", null, TestTimes.UtcNow);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Throw_When_Intent_Is_Opened_On_A_Payment_Already_Started()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);
        payment.OpenCheckout("cs_1", "https://checkout.test/cs_1", TestTimes.UtcNow);

        // Act
        var act = () => payment.OpenIntent("pi_1", "gcash", 646.14m, null, "https://gcash.test", null, TestTimes.UtcNow);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Fail_And_Drop_Qr_Code_When_Pending_Attempt_Ends_Unpaid()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);
        payment.OpenIntent("pi_1", "qrph", 639.61m, "data:image/png;base64,QR", null, TestTimes.UtcNow, TestTimes.UtcNow);

        // Act
        payment.EndUnpaid("qrph.expired", TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            payment.Status.Should().Be(OnlinePaymentStatus.Failed);
            payment.FailureReason.Should().Be("qrph.expired");
            payment.QrCodeImage.Should().BeNull();
            payment.IsPending.Should().BeFalse();
            payment.CanStillBePaid.Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Stay_Paid_When_A_Paid_Attempt_Is_Told_It_Ended_Unpaid()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);
        payment.RecordPaid("pay_1", "gcash", 646.14m, 16.14m, 630m, TestTimes.UtcNow, TestTimes.UtcNow);
        payment.Settle(TestTimes.UtcNow);

        // Act
        payment.EndUnpaid("payment.failed", TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            payment.Status.Should().Be(OnlinePaymentStatus.Paid);
            payment.FailureReason.Should().BeNull();
            payment.CanStillBePaid.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Clear_Failure_When_A_Failed_Attempt_Is_Paid_After_All()
    {
        // Arrange
        var payment = Payment(venueAmount: 600m, platformFee: 30m);
        payment.EndUnpaid("payment.failed", TestTimes.UtcNow);

        // Act
        payment.RecordPaid("pay_1", "gcash", 646.14m, 16.14m, 630m, TestTimes.UtcNow, TestTimes.UtcNow);

        // Assert
        payment.FailureReason.Should().BeNull();
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
