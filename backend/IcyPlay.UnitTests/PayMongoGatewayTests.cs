using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions.Execution;
using IcyPlay.Application.Payments;
using IcyPlay.Infrastructure.Payments;
using IcyPlay.UnitTests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq.Protected;

namespace IcyPlay.UnitTests;

public sealed class PayMongoGatewayTests
{
    private const string WebhookSecret = "whsk_test_secret";

    [Fact]
    public void Should_Read_Paid_Checkout_In_Pesos_When_Test_Signature_Matches()
    {
        // Arrange
        var gateway = Gateway();
        var body = PaidEvent(liveMode: false);
        var header = Signature(body, TestTimes.UtcNow, liveMode: false);

        // Act
        var received = gateway.ReadEvent(body, header);

        // Assert
        using (new AssertionScope())
        {
            received.Should().NotBeNull();
            received!.EventId.Should().Be("evt_1");
            received.EventType.Should().Be(PayMongoGateway.CheckoutPaidEvent);
            received.CheckoutSessionId.Should().Be("cs_1");
            received.Payment!.PaymentId.Should().Be("pay_1");
            received.Payment.PaymentMethod.Should().Be("qrph");
            received.Payment.Amount.Should().Be(639.60m);
            received.Payment.Fee.Should().Be(9.60m);
            received.Payment.NetAmount.Should().Be(630m);
            received.Payment.PaidAt.Should().Be(TestTimes.UtcNow.AddMinutes(-1));
        }
    }

    [Fact]
    public void Should_Return_Null_When_Body_Was_Changed_After_Signing()
    {
        // Arrange
        var gateway = Gateway();
        var body = PaidEvent(liveMode: false);
        var header = Signature(body, TestTimes.UtcNow, liveMode: false);
        var tampered = body.Replace("63960", "100", StringComparison.Ordinal);

        // Act
        var received = gateway.ReadEvent(tampered, header);

        // Assert
        received.Should().BeNull();
    }

    [Fact]
    public void Should_Return_Null_When_Live_Event_Carries_Only_A_Test_Signature()
    {
        // Arrange
        var gateway = Gateway();
        var body = PaidEvent(liveMode: true);
        var header = Signature(body, TestTimes.UtcNow, liveMode: false);

        // Act
        var received = gateway.ReadEvent(body, header);

        // Assert
        received.Should().BeNull();
    }

    [Fact]
    public void Should_Return_Null_When_Signature_Is_Older_Than_Tolerance()
    {
        // Arrange
        var gateway = Gateway();
        var body = PaidEvent(liveMode: false);
        var header = Signature(body, TestTimes.UtcNow.AddMinutes(-11), liveMode: false);

        // Act
        var received = gateway.ReadEvent(body, header);

        // Assert
        received.Should().BeNull();
    }

    [Fact]
    public void Should_Return_Null_When_Signature_Header_Is_Missing()
    {
        // Arrange
        var gateway = Gateway();

        // Act
        var received = gateway.ReadEvent(PaidEvent(liveMode: false), null);

        // Assert
        received.Should().BeNull();
    }

    [Fact]
    public async Task Should_Send_Centavos_And_Pass_Fees_On_When_Checkout_Is_Created()
    {
        // Arrange
        HttpRequestMessage? sent = null;
        string? sentBody = null;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
            {
                sent = request;
                sentBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"cs_9","attributes":{"checkout_url":"https://checkout.paymongo.com/cs_9"}}}""",
                    Encoding.UTF8,
                    "application/json")
            });
        var gateway = Gateway(new HttpClient(handler.Object));
        var request = new CheckoutRequest(
            "BK-0001",
            "Court 1 at Demo Sports Center",
            [new CheckoutLineItem("Court 1, 2 hours", 600m), new CheckoutLineItem("IcyPlay platform fee", 30m)],
            "https://icyplay.test/bookings/1?payment=success",
            "https://icyplay.test/bookings/1?payment=cancelled",
            new Dictionary<string, string> { ["payment_id"] = "p1" });

        // Act
        var created = await gateway.CreateCheckoutAsync(request, CancellationToken.None);

        // Assert
        using var document = JsonDocument.Parse(sentBody!);
        var attributes = document.RootElement.GetProperty("data").GetProperty("attributes");

        using (new AssertionScope())
        {
            created.SessionId.Should().Be("cs_9");
            created.CheckoutUrl.Should().Be("https://checkout.paymongo.com/cs_9");
            sent!.RequestUri!.ToString().Should().Be("https://api.paymongo.com/v2/checkout_sessions");
            sent.Headers.Authorization!.Scheme.Should().Be("Basic");
            attributes.GetProperty("pass_on_fees").GetBoolean().Should().BeTrue();
            attributes.GetProperty("line_items")[0].GetProperty("amount").GetInt64().Should().Be(60000);
            attributes.GetProperty("line_items")[1].GetProperty("amount").GetInt64().Should().Be(3000);
            attributes.GetProperty("reference_number").GetString().Should().Be("BK-0001");
        }
    }

    [Fact]
    public async Task Should_Throw_When_PayMongo_Refuses_The_Checkout()
    {
        // Arrange
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"errors":[{"code":"parameter_required"}]}""")
            });
        var gateway = Gateway(new HttpClient(handler.Object));
        var request = new CheckoutRequest(
            "BK-0001",
            "Court",
            [new CheckoutLineItem("Court", 600m)],
            "https://icyplay.test/s",
            "https://icyplay.test/c",
            new Dictionary<string, string>());

        // Act
        var act = () => gateway.CreateCheckoutAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    private static PayMongoGateway Gateway(HttpClient? http = null) =>
        new(
            http ?? new HttpClient(new Mock<HttpMessageHandler>(MockBehavior.Strict).Object),
            Options.Create(new PayMongoOptions
            {
                SecretKey = "sk_test_key",
                WebhookSecret = WebhookSecret
            }),
            new FixedTimeProvider(TestTimes.UtcNow),
            NullLogger<PayMongoGateway>.Instance);

    private static string PaidEvent(bool liveMode)
    {
        var paidAt = TestTimes.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();

        return """
            {"data":{"id":"evt_1","type":"event","attributes":{"type":"checkout_session.payment.paid","livemode":LIVEMODE,"data":{"id":"cs_1","type":"checkout_session","attributes":{"reference_number":"BK-0001","payments":[{"id":"pay_1","type":"payment","attributes":{"amount":63960,"fee":960,"net_amount":63000,"status":"paid","paid_at":PAIDAT,"source":{"type":"qrph"}}}]}}}}}
            """
            .Replace("LIVEMODE", liveMode ? "true" : "false", StringComparison.Ordinal)
            .Replace("PAIDAT", paidAt.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string Signature(string body, DateTimeOffset signedAt, bool liveMode)
    {
        var timestamp = signedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        var signature = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}")));

        return liveMode ? $"t={timestamp},te=,li={signature}" : $"t={timestamp},te={signature},li=";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
