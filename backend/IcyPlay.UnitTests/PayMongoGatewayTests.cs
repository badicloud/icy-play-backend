using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions.Execution;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Payments;
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
    public async Task Should_Offer_Only_The_Methods_Asked_For_When_Checkout_Names_Them()
    {
        // Arrange
        var paymongo = new FakePayMongo()
            .Answer("/v2/checkout_sessions", """{"data":{"id":"cs_card","attributes":{"checkout_url":"https://checkout.paymongo.com/cs_card"}}}""");
        var gateway = Gateway(new HttpClient(paymongo));
        var request = new CheckoutRequest(
            "BK-0001",
            "Court",
            [new CheckoutLineItem("Court", 600m)],
            "https://icyplay.test/s",
            "https://icyplay.test/c",
            new Dictionary<string, string>(),
            ["card"]);

        // Act
        await gateway.CreateCheckoutAsync(request, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            paymongo.Body(0).GetProperty("payment_method_types").EnumerateArray().Select(type => type.GetString()).Should().Equal("card");
            paymongo.Body(0).GetProperty("pass_on_fees").GetBoolean().Should().BeTrue();
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

    [Theory]
    [InlineData(null, CheckoutExperience.Hosted)]
    [InlineData("", CheckoutExperience.Hosted)]
    [InlineData("Hosted", CheckoutExperience.Hosted)]
    [InlineData("integrated", CheckoutExperience.Integrated)]
    [InlineData(" Integrated ", CheckoutExperience.Integrated)]
    [InlineData("Intergrated", CheckoutExperience.Hosted)]
    public void Should_Use_Known_Experience_Or_Hosted_When_Checkout_Setting_Is_Read(
        string? setting,
        string expected)
    {
        // Arrange
        var gateway = Gateway(options: new PayMongoOptions { SecretKey = "sk_test_key", CheckoutExperience = setting! });

        // Act
        var experience = gateway.CheckoutExperience;

        // Assert
        experience.Should().Be(expected);
    }

    [Fact]
    public void Should_Quote_Each_Method_With_Its_Published_Rate_When_Rates_Are_Not_Configured()
    {
        // Arrange
        var gateway = Gateway();

        // Act
        var quotes = gateway.QuoteMethods(630m);

        // Assert
        quotes.Should().Equal(
            new PaymentMethodQuote("qrph", 630m, 9.61m, 639.61m),
            new PaymentMethodQuote("gcash", 630m, 16.14m, 646.14m),
            new PaymentMethodQuote("paymaya", 630m, 12.90m, 642.90m));
    }

    [Fact]
    public void Should_Quote_Only_Offered_Methods_With_A_Rate_When_Rates_Are_Configured()
    {
        // Arrange
        var options = new PayMongoOptions
        {
            SecretKey = "sk_test_key",
            PaymentMethodTypes = ["gcash", "card", "qrph"]
        };
        options.FeeRates["gcash"] = new PayMongoFeeRate { Percent = 2m, Fixed = 1m };
        var gateway = Gateway(options: options);

        // Act
        var quotes = gateway.QuoteMethods(100m);

        // Assert
        using (new AssertionScope())
        {
            quotes.Select(quote => quote.Method).Should().Equal("gcash", "qrph");
            quotes[0].Total.Should().Be(new ProcessingFeeRate(2m, 1m).GrossUp(100m));
            quotes[0].ProcessingFee.Should().Be(quotes[0].Total - 100m);
        }
    }

    [Fact]
    public async Task Should_Return_Qr_Code_As_Data_Url_When_Qr_Ph_Intent_Is_Started()
    {
        // Arrange
        var paymongo = new FakePayMongo()
            .Answer("/v1/payment_intents", """{"data":{"id":"pi_1","attributes":{"status":"awaiting_payment_method"}}}""")
            .Answer("/v1/payment_methods", """{"data":{"id":"pm_1","attributes":{"type":"qrph"}}}""")
            .Answer("/v1/payment_intents/pi_1/attach", """{"data":{"id":"pi_1","attributes":{"status":"awaiting_next_action","next_action":{"type":"consume_qr","code":{"id":"code_1","image_url":"iVBORw0KGgo="}}}}}""");
        var gateway = Gateway(new HttpClient(paymongo));

        // Act
        var started = await gateway.StartPaymentIntentAsync(IntentRequest("qrph", 639.61m), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            started.PaymentIntentId.Should().Be("pi_1");
            started.QrCodeImage.Should().Be("data:image/png;base64,iVBORw0KGgo=");
            started.RedirectUrl.Should().BeNull();
            started.ExpiresAt.Should().Be(TestTimes.UtcNow.AddMinutes(10));
            paymongo.Paths.Should().Equal("/v1/payment_intents", "/v1/payment_methods", "/v1/payment_intents/pi_1/attach");
            paymongo.Body(0).GetProperty("amount").GetInt64().Should().Be(63961);
            paymongo.Body(0).GetProperty("payment_method_allowed")[0].GetString().Should().Be("qrph");
            paymongo.Body(1).GetProperty("expiry_seconds").GetInt32().Should().Be(600);
            paymongo.Body(2).GetProperty("payment_method").GetString().Should().Be("pm_1");
            paymongo.Body(2).TryGetProperty("return_url", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Should_Return_E_Wallet_Page_When_GCash_Intent_Is_Started()
    {
        // Arrange
        var paymongo = new FakePayMongo()
            .Answer("/v1/payment_intents", """{"data":{"id":"pi_2","attributes":{"status":"awaiting_payment_method"}}}""")
            .Answer("/v1/payment_methods", """{"data":{"id":"pm_2","attributes":{"type":"gcash"}}}""")
            .Answer("/v1/payment_intents/pi_2/attach", """{"data":{"id":"pi_2","attributes":{"status":"awaiting_next_action","next_action":{"type":"redirect","redirect":{"url":"https://gcash.test/authorize","return_url":"https://icyplay.test/bookings/1"}}}}}""");
        var gateway = Gateway(new HttpClient(paymongo));

        // Act
        var started = await gateway.StartPaymentIntentAsync(IntentRequest("gcash", 646.14m), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            started.RedirectUrl.Should().Be("https://gcash.test/authorize");
            started.QrCodeImage.Should().BeNull();
            started.ExpiresAt.Should().BeNull();
            paymongo.Body(1).TryGetProperty("expiry_seconds", out _).Should().BeFalse();
            paymongo.Body(2).GetProperty("return_url").GetString().Should().Be("https://icyplay.test/bookings/1?payment=returned");
        }
    }

    [Fact]
    public async Task Should_Throw_When_PayMongo_Gives_No_Next_Step_For_An_Intent()
    {
        // Arrange
        var paymongo = new FakePayMongo()
            .Answer("/v1/payment_intents", """{"data":{"id":"pi_3","attributes":{}}}""")
            .Answer("/v1/payment_methods", """{"data":{"id":"pm_3","attributes":{}}}""")
            .Answer("/v1/payment_intents/pi_3/attach", """{"data":{"id":"pi_3","attributes":{"status":"processing","next_action":null}}}""");
        var gateway = Gateway(new HttpClient(paymongo));

        // Act
        var act = () => gateway.StartPaymentIntentAsync(IntentRequest("gcash", 646.14m), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Should_Read_Paid_Payment_When_Intent_Has_Been_Paid()
    {
        // Arrange
        var paymongo = new FakePayMongo()
            .Answer("/v1/payment_intents/pi_1", """{"data":{"id":"pi_1","attributes":{"status":"succeeded","payments":[{"id":"pay_9","type":"payment","attributes":{"amount":64614,"fee":1614,"net_amount":63000,"status":"paid","paid_at":1760000000,"source":{"type":"gcash"}}}]}}}""");
        var gateway = Gateway(new HttpClient(paymongo));

        // Act
        var paid = await gateway.GetPaidIntentPaymentAsync("pi_1", CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            paid.Should().NotBeNull();
            paid!.PaymentId.Should().Be("pay_9");
            paid.PaymentMethod.Should().Be("gcash");
            paid.Amount.Should().Be(646.14m);
            paid.Fee.Should().Be(16.14m);
            paid.NetAmount.Should().Be(630m);
        }
    }

    [Fact]
    public void Should_Read_Intent_And_Payment_When_Payment_Paid_Event_Is_Signed()
    {
        // Arrange
        var gateway = Gateway();
        var body = """
            {"data":{"id":"evt_2","type":"event","attributes":{"type":"payment.paid","livemode":false,"data":{"id":"pay_2","type":"payment","attributes":{"amount":63961,"fee":961,"net_amount":63000,"status":"paid","paid_at":1760000000,"payment_intent_id":"pi_1","source":{"type":"qrph"}}}}}}
            """;
        var header = Signature(body, TestTimes.UtcNow, liveMode: false);

        // Act
        var received = gateway.ReadEvent(body, header);

        // Assert
        using (new AssertionScope())
        {
            received.Should().NotBeNull();
            received!.EventType.Should().Be(PayMongoGateway.PaymentPaidEvent);
            received.PaymentIntentId.Should().Be("pi_1");
            received.CheckoutSessionId.Should().BeNull();
            received.Payment!.PaymentId.Should().Be("pay_2");
            received.Payment.NetAmount.Should().Be(630m);
        }
    }

    [Fact]
    public void Should_Read_Intent_And_Reason_Without_Payment_When_Payment_Failed_Event_Is_Signed()
    {
        // Arrange
        var gateway = Gateway();
        var body = """
            {"data":{"id":"evt_3","type":"event","attributes":{"type":"payment.failed","livemode":false,"data":{"id":"pay_3","type":"payment","attributes":{"amount":64614,"status":"failed","failed_code":"insufficient_funds","payment_intent_id":"pi_2"}}}}}
            """;
        var header = Signature(body, TestTimes.UtcNow, liveMode: false);

        // Act
        var received = gateway.ReadEvent(body, header);

        // Assert
        using (new AssertionScope())
        {
            received.Should().NotBeNull();
            received!.EventType.Should().Be(PayMongoGateway.PaymentFailedEvent);
            received.PaymentIntentId.Should().Be("pi_2");
            received.FailureCode.Should().Be("insufficient_funds");
            received.Payment.Should().BeNull();
        }
    }

    private static PaymentIntentRequest IntentRequest(string method, decimal amount) =>
        new(
            method,
            amount,
            "Court 1 at Demo Sports Center",
            "https://icyplay.test/bookings/1?payment=returned",
            TimeSpan.FromMinutes(10),
            new Dictionary<string, string> { ["payment_id"] = "p1" });

    private static PayMongoGateway Gateway(HttpClient? http = null, PayMongoOptions? options = null) =>
        new(
            http ?? new HttpClient(new Mock<HttpMessageHandler>(MockBehavior.Strict).Object),
            Options.Create(options ?? new PayMongoOptions
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

    /// <summary>
    /// PayMongo's API as far as one test needs it: a canned answer per path,
    /// and every call it was sent, in order, with what was inside it.
    /// </summary>
    private sealed class FakePayMongo : HttpMessageHandler
    {
        private readonly Dictionary<string, string> answers = new(StringComparer.Ordinal);
        private readonly List<string?> bodies = [];

        public List<string> Paths { get; } = [];

        public FakePayMongo Answer(string path, string json)
        {
            answers[path] = json;
            return this;
        }

        /// <summary>The <c>data.attributes</c> of the call at that position.</summary>
        public JsonElement Body(int call) =>
            JsonDocument.Parse(bodies[call]!).RootElement.GetProperty("data").GetProperty("attributes");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

            return answers.TryGetValue(path, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"errors":[]}""") };
        }
    }
}
