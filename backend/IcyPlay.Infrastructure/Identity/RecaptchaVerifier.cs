using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using IcyPlay.Application.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Identity;

public sealed class RecaptchaVerifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<RecaptchaVerifier> logger) : IRecaptchaVerifier
{
    public async Task<bool> VerifyAsync(
        string token,
        string expectedAction,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        var secret = configuration["Recaptcha:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            logger.LogError("The reCAPTCHA secret key is not configured.");
            return false;
        }

        var allowedHostnames = configuration.GetSection("Recaptcha:AllowedHostnames").Get<string[]>() ?? [];
        if (allowedHostnames.Length == 0)
        {
            logger.LogError("No allowed reCAPTCHA hostnames are configured.");
            return false;
        }

        var minimumScore = configuration.GetValue("Recaptcha:MinimumScore", 0.5d);

        try
        {
            var verificationFields = new Dictionary<string, string>
            {
                ["secret"] = secret,
                ["response"] = token
            };

            if (IPAddress.TryParse(remoteIp, out var parsedRemoteIp) &&
                !IPAddress.IsLoopback(parsedRemoteIp))
            {
                verificationFields["remoteip"] = parsedRemoteIp.ToString();
            }

            using var content = new FormUrlEncodedContent(verificationFields);
            using var response = await httpClient.PostAsync(
                "https://www.google.com/recaptcha/api/siteverify",
                content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "reCAPTCHA verification endpoint returned HTTP status {StatusCode}.",
                    (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<RecaptchaResponse>(cancellationToken);
            if (result is null)
            {
                logger.LogWarning("reCAPTCHA verification endpoint returned an empty response.");
                return false;
            }

            if (!result.Success)
            {
                logger.LogWarning(
                    "Google rejected the reCAPTCHA token. ErrorCodes: {ErrorCodes}.",
                    result.ErrorCodes is { Length: > 0 }
                        ? string.Join(", ", result.ErrorCodes)
                        : "None");
                return false;
            }

            if (result.Score < minimumScore)
            {
                logger.LogWarning(
                    "reCAPTCHA score {Score} is below the configured minimum {MinimumScore}.",
                    result.Score,
                    minimumScore);
                return false;
            }

            if (!string.Equals(result.Action, expectedAction, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "reCAPTCHA action mismatch. Expected: {ExpectedAction}, Actual: {ActualAction}.",
                    expectedAction,
                    result.Action);
                return false;
            }

            if (!allowedHostnames.Contains(result.Hostname, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "reCAPTCHA hostname {Hostname} is not in the allowed hostname list.",
                    result.Hostname);
                return false;
            }

            return true;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The reCAPTCHA verification request failed.");
            return false;
        }
    }

    private sealed record RecaptchaResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
