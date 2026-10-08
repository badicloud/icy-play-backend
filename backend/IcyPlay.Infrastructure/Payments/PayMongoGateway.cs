using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IcyPlay.Application.Payments;
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

    private PayMongoOptions Settings => options.Value;

    public string Provider => ProviderName;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.SecretKey);

    private bool CanReadEvents => !string.IsNullOrWhiteSpace(Settings.WebhookSecret);

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
                    ["payment_method_types"] = Settings.PaymentMethodTypes,
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
            GatewayPaidPayment? payment = null;

            if (attributes.TryGetProperty("data", out var resource) && resource.ValueKind == JsonValueKind.Object)
            {
                sessionId = resource.TryGetProperty("id", out var sessionElement) ? sessionElement.GetString() : null;

                if (resource.TryGetProperty("attributes", out var resourceAttributes) &&
                    resourceAttributes.TryGetProperty("payments", out var payments) &&
                    payments.ValueKind == JsonValueKind.Array)
                {
                    payment = payments.EnumerateArray()
                        .Select(ReadPayment)
                        .FirstOrDefault(candidate => candidate is not null);
                }
            }

            return new GatewayEvent(eventId, eventType, liveMode, sessionId, payment);
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
