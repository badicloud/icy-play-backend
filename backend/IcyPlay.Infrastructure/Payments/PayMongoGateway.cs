using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Payments;

/// <summary>
/// PayMongo, spoken to over its REST API.
///
/// Money crosses this boundary as pesos on our side and centavos on theirs.
/// The conversion happens here and nowhere else, so nothing upstream ever
/// sees an amount a hundred times too large.
/// </summary>
public sealed class PayMongoGateway(
    HttpClient http,
    IOptions<PayMongoOptions> options,
    TimeProvider timeProvider,
    ILogger<PayMongoGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "PayMongo";

    /// <summary>The one event that says a checkout was paid.</summary>
    public const string CheckoutPaidEvent = "checkout_session.payment.paid";

    /// <summary>A payment started on our own page went through.</summary>
    public const string PaymentPaidEvent = "payment.paid";

    /// <summary>A payment started on our own page was declined, or the customer gave up at the e-wallet.</summary>
    public const string PaymentFailedEvent = "payment.failed";

    /// <summary>A QR Ph code shown on our own page ran out before anyone paid it.</summary>
    public const string QrPhExpiredEvent = "qrph.expired";

    /// <summary>What PayMongo allows for a QR Ph code's life, in seconds.</summary>
    private const int ShortestQrSeconds = 60;
    private const int LongestQrSeconds = 9000;

    private PayMongoOptions Settings => options.Value;

    public string Provider => ProviderName;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.SecretKey);

    private bool CanReadEvents => !string.IsNullOrWhiteSpace(Settings.WebhookSecret);

    public string CheckoutExperience => Application.Payments.CheckoutExperience.From(Settings.CheckoutExperience);

    public IReadOnlyList<PaymentMethodQuote> QuoteMethods(decimal amountDue) =>
        Settings.PaymentMethodTypes
            .Where(Settings.FeeRates.ContainsKey)
            .Select(method =>
            {
                var rate = Settings.FeeRates[method];
                var total = new ProcessingFeeRate(rate.Percent, rate.Fixed).GrossUp(amountDue);
                return new PaymentMethodQuote(method, amountDue, total - amountDue, total);
            })
            .ToArray();

    public async Task<CheckoutSessionCreated> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new
        {
            data = new
            {
                attributes = new Dictionary<string, object?>
                {
                    ["line_items"] = request.LineItems.Select(item => new
                    {
                        name = item.Name,
                        amount = Centavos(item.Amount),
                        currency = "PHP",
                        quantity = 1
                    }).ToArray(),
                    ["payment_method_types"] = request.PaymentMethodTypes ?? Settings.PaymentMethodTypes,
                    ["success_url"] = request.SuccessUrl,
                    ["cancel_url"] = request.CancelUrl,
                    ["reference_number"] = request.ReferenceNumber,
                    ["description"] = request.Description,
                    ["send_email_receipt"] = true,
                    ["show_line_items"] = true,
                    ["show_description"] = true,
                    // The gateway's fee goes on top, for the customer to pay,
                    // so what reaches the venue is exactly what the court costs.
                    ["pass_on_fees"] = true,
                    ["metadata"] = request.Metadata
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(Settings.BaseUrl), Settings.CheckoutPath))
        {
            Content = JsonContent.Create(body)
        };

        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Settings.SecretKey}:")));

        using var response = await http.SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // The body is PayMongo's error list, which names the field it
            // disliked. It holds nothing secret, and without it a 400 is a guess.
            logger.LogError(
                "PayMongo refused checkout {Reference}: {Status} {Body}",
                request.ReferenceNumber,
                (int)response.StatusCode,
                text);

            throw new HttpRequestException($"PayMongo refused the checkout with {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(text);
        var data = document.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetString();
        var url = data.GetProperty("attributes").GetProperty("checkout_url").GetString();

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
        {
            throw new HttpRequestException("PayMongo answered without a checkout id or URL.");
        }

        return new CheckoutSessionCreated(id, url);
    }

    public async Task<GatewayPaidPayment?> GetPaidPaymentAsync(string checkoutSessionId, CancellationToken ct)
    {
        // v1 reads a checkout session made through v2 as well; v2 is only
        // needed to create one with the fee passed on.
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(Settings.BaseUrl), $"/v1/checkout_sessions/{Uri.EscapeDataString(checkoutSessionId)}"));

        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Settings.SecretKey}:")));

        using var response = await http.SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "PayMongo would not say how checkout {SessionId} stands: {Status} {Body}",
                checkoutSessionId,
                (int)response.StatusCode,
                text);

            throw new HttpRequestException($"PayMongo answered {(int)response.StatusCode} for a checkout.");
        }

        using var document = JsonDocument.Parse(text);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("attributes", out var attributes) ||
            !attributes.TryGetProperty("payments", out var payments) ||
            payments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return payments.EnumerateArray()
            .Select(ReadPayment)
            .FirstOrDefault(candidate => candidate is not null);
    }

    /// <summary>
    /// Three calls, the way PayMongo has it: a payment intent for the amount,
    /// a payment method of the one type, then the two joined — which is what
    /// hands back the QR code or the e-wallet's page. All server side, with
    /// the secret key: the browser never talks to PayMongo.
    /// </summary>
    public async Task<PaymentIntentStarted> StartPaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), request.Amount, "Only something owed can be charged.");
        }

        var isQr = string.Equals(request.Method, "qrph", StringComparison.OrdinalIgnoreCase);

        using var intent = await CallAsync(
            HttpMethod.Post,
            "/v1/payment_intents",
            new Dictionary<string, object?>
            {
                ["amount"] = Centavos(request.Amount),
                ["currency"] = "PHP",
                ["payment_method_allowed"] = new[] { request.Method },
                ["description"] = request.Description,
                ["metadata"] = request.Metadata
            },
            ct);
        var intentId = intent.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new HttpRequestException("PayMongo answered without a payment intent id.");

        var qrSeconds = Math.Clamp((int)request.QrLifetime.TotalSeconds, ShortestQrSeconds, LongestQrSeconds);
        var method = new Dictionary<string, object?> { ["type"] = request.Method };

        if (isQr)
        {
            method["expiry_seconds"] = qrSeconds;
        }

        using var created = await CallAsync(HttpMethod.Post, "/v1/payment_methods", method, ct);
        var methodId = created.RootElement.GetProperty("data").GetProperty("id").GetString()
            ?? throw new HttpRequestException("PayMongo answered without a payment method id.");

        var attach = new Dictionary<string, object?> { ["payment_method"] = methodId };

        if (!isQr)
        {
            // Where the e-wallet sends the customer back to — paid, declined
            // or gave up — so nobody is left on a page of theirs with no way home.
            attach["return_url"] = request.ReturnUrl;
        }

        using var attached = await CallAsync(
            HttpMethod.Post,
            $"/v1/payment_intents/{Uri.EscapeDataString(intentId)}/attach",
            attach,
            ct);

        var nextAction = attached.RootElement.GetProperty("data").GetProperty("attributes").TryGetProperty("next_action", out var next)
            && next.ValueKind == JsonValueKind.Object
                ? next
                : default;

        var redirectUrl = nextAction.ValueKind == JsonValueKind.Object
            && nextAction.TryGetProperty("redirect", out var redirect)
            && redirect.ValueKind == JsonValueKind.Object
            && redirect.TryGetProperty("url", out var url)
                ? url.GetString()
                : null;

        var qrImage = nextAction.ValueKind == JsonValueKind.Object
            && nextAction.TryGetProperty("code", out var code)
            && code.ValueKind == JsonValueKind.Object
            && code.TryGetProperty("image_url", out var image)
                ? image.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(redirectUrl) && string.IsNullOrWhiteSpace(qrImage))
        {
            throw new HttpRequestException($"PayMongo gave no next step for {request.Method} on {intentId}.");
        }

        return new PaymentIntentStarted(
            intentId,
            string.IsNullOrWhiteSpace(redirectUrl) ? null : redirectUrl,
            string.IsNullOrWhiteSpace(qrImage) ? null : AsDataUrl(qrImage),
            isQr ? timeProvider.GetUtcNow().AddSeconds(qrSeconds) : null);
    }

    public async Task<GatewayPaidPayment?> GetPaidIntentPaymentAsync(string paymentIntentId, CancellationToken ct)
    {
        // The payments on an intent come back only to the secret key — which
        // is the only key this server uses.
        using var document = await CallAsync(
            HttpMethod.Get,
            $"/v1/payment_intents/{Uri.EscapeDataString(paymentIntentId)}",
            body: null,
            ct);

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("attributes", out var attributes) ||
            !attributes.TryGetProperty("payments", out var payments) ||
            payments.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return payments.EnumerateArray()
            .Select(ReadPayment)
            .FirstOrDefault(candidate => candidate is not null);
    }

    public GatewayEvent? ReadEvent(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrEmpty(rawBody) || string.IsNullOrWhiteSpace(signatureHeader) || !CanReadEvents)
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("attributes", out var attributes))
            {
                return null;
            }

            var liveMode = attributes.TryGetProperty("livemode", out var live) && live.ValueKind == JsonValueKind.True;

            if (!SignatureHolds(rawBody, signatureHeader, liveMode))
            {
                return null;
            }

            var eventId = data.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            var eventType = attributes.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType))
            {
                return null;
            }

            string? sessionId = null;
            string? intentId = null;
            string? failureCode = null;
            GatewayPaidPayment? payment = null;

            if (attributes.TryGetProperty("data", out var resource) && resource.ValueKind == JsonValueKind.Object)
            {
                var resourceId = resource.TryGetProperty("id", out var idOfResource) ? idOfResource.GetString() : null;
                var resourceType = resource.TryGetProperty("type", out var typeOfResource) ? typeOfResource.GetString() : null;
                var resourceAttributes = resource.TryGetProperty("attributes", out var found) && found.ValueKind == JsonValueKind.Object
                    ? found
                    : default;

                switch (resourceType)
                {
                    case "payment":
                        // A payment started on our own page: the event is the
                        // payment itself, and names the intent it belongs to.
                        payment = ReadPayment(resource);
                        intentId = StringOf(resourceAttributes, "payment_intent_id");
                        failureCode = StringOf(resourceAttributes, "failed_code");
                        break;

                    case "payment_intent":
                        intentId = resourceId;
                        break;

                    case not (null or "checkout_session"):
                        // Anything else — a QR code that expired, say — is
                        // ours only through the intent it names, if it names one.
                        intentId = StringOf(resourceAttributes, "payment_intent_id")
                            ?? (resourceId?.StartsWith("pi_", StringComparison.Ordinal) == true ? resourceId : null);
                        break;

                    default:
                        // A hosted checkout, and the payments made on it.
                        sessionId = resourceId;

                        if (resourceAttributes.ValueKind == JsonValueKind.Object &&
                            resourceAttributes.TryGetProperty("payments", out var payments) &&
                            payments.ValueKind == JsonValueKind.Array)
                        {
                            payment = payments.EnumerateArray()
                                .Select(ReadPayment)
                                .FirstOrDefault(candidate => candidate is not null);
                        }

                        break;
                }
            }

            return new GatewayEvent(eventId, eventType, liveMode, sessionId, payment, intentId, failureCode);
        }
    }

    /// <summary>
    /// PayMongo signs <c>{t}.{raw body}</c> with the webhook's secret and sends
    /// <c>t=…,te=…,li=…</c>: one signature for test mode and one for live. The
    /// one that matters is the one for the mode the event says it is in.
    /// </summary>
    private bool SignatureHolds(string rawBody, string header, bool liveMode)
    {
        var parts = header
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First()[1], StringComparer.Ordinal);

        if (!parts.TryGetValue("t", out var timestamp) ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);

        if ((timeProvider.GetUtcNow() - signedAt).Duration() > TimeSpan.FromSeconds(Settings.SignatureToleranceSeconds))
        {
            return false;
        }

        if (!parts.TryGetValue(liveMode ? "li" : "te", out var sent) || string.IsNullOrWhiteSpace(sent))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Settings.WebhookSecret));
        var expected = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{rawBody}")));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(sent.ToLowerInvariant()));
    }

    /// <summary>
    /// One call to PayMongo's v1 API with the secret key, wrapped the way it
    /// expects (<c>data.attributes</c>). A refusal is logged with PayMongo's
    /// own words — which name the field it disliked and hold nothing secret —
    /// and thrown, so the caller can tell the customer it did not work.
    /// </summary>
    private async Task<JsonDocument> CallAsync(
        HttpMethod method,
        string path,
        IReadOnlyDictionary<string, object?>? body,
        CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(new Uri(Settings.BaseUrl), path));

        if (body is not null)
        {
            message.Content = JsonContent.Create(new
            {
                data = new
                {
                    attributes = body
                }
            });
        }

        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Settings.SecretKey}:")));

        using var response = await http.SendAsync(message, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "PayMongo refused {Method} {Path}: {Status} {Body}",
                method,
                path,
                (int)response.StatusCode,
                text);

            throw new HttpRequestException($"PayMongo answered {(int)response.StatusCode} to {method} {path}.");
        }

        return JsonDocument.Parse(text);
    }

    private static string? StringOf(JsonElement attributes, string name) =>
        attributes.ValueKind == JsonValueKind.Object &&
        attributes.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>
    /// PayMongo sends the QR code as Base64, said to be ready for an
    /// <c>img</c> tag. Made so either way, whether or not it came as a data URL.
    /// </summary>
    private static string AsDataUrl(string image) =>
        image.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? image : $"data:image/png;base64,{image}";

    private static GatewayPaidPayment? ReadPayment(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var idElement) ||
            !element.TryGetProperty("attributes", out var attributes))
        {
            return null;
        }

        var status = attributes.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;

        if (!string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var method = attributes.TryGetProperty("payment_method_used", out var used) && used.ValueKind == JsonValueKind.String
            ? used.GetString()
            : attributes.TryGetProperty("source", out var source) &&
                source.ValueKind == JsonValueKind.Object &&
                source.TryGetProperty("type", out var sourceType)
                ? sourceType.GetString()
                : null;

        DateTimeOffset? paidAt = attributes.TryGetProperty("paid_at", out var paid) && paid.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(paid.GetInt64())
            : null;

        return new GatewayPaidPayment(
            idElement.GetString() ?? string.Empty,
            method,
            Pesos(attributes, "amount"),
            Pesos(attributes, "fee"),
            Pesos(attributes, "net_amount"),
            paidAt);
    }

    private static decimal Pesos(JsonElement attributes, string name) =>
        attributes.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64() / 100m
            : 0m;

    /// <summary>
    /// Pesos to centavos. Rounded rather than truncated, though every amount
    /// here is already to the centavo.
    /// </summary>
    private static long Centavos(decimal pesos) =>
        (long)Math.Round(pesos * 100m, 0, MidpointRounding.AwayFromZero);
}
