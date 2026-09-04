using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IcyPlay.Application.Email;
using IcyPlay.Domain.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Email;

public sealed class MailjetTransactionalEmailSender(
    HttpClient httpClient,
    IEmailTemplateStore templateStore,
    IOptions<MailjetOptions> options,
    ILogger<MailjetTransactionalEmailSender> logger) : ITransactionalEmailSender
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null
    };

    private readonly MailjetOptions mailjetOptions = options.Value;

    public async Task SendAsync(
        TransactionalEmailMessage message,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        var template = await templateStore.GetActiveAsync(
            message.TemplateKey,
            EmailProviderName.Mailjet,
            cancellationToken);
        if (template is null)
        {
            throw new InvalidOperationException(
                $"No active Mailjet template is configured for '{message.TemplateKey}'.");
        }

        var payload = new
        {
            Messages = new[]
            {
                new
                {
                    From = new
                    {
                        Email = mailjetOptions.SenderEmail,
                        Name = mailjetOptions.SenderName
                    },
                    To = new[]
                    {
                        new
                        {
                            Email = message.RecipientEmail,
                            Name = message.RecipientName
                        }
                    },
                    TemplateID = template.ExternalTemplateId,
                    TemplateLanguage = true,
                    Subject = template.Subject,
                    Variables = message.Variables
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "send")
        {
            Content = JsonContent.Create(payload, options: SerializerOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{mailjetOptions.ApiKey}:{mailjetOptions.ApiSecret}")));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorDetails = await ReadErrorDetailsAsync(response, cancellationToken);
            logger.LogError(
                "Mailjet rejected transactional email template {TemplateKey} with HTTP status {StatusCode}. Details: {ErrorDetails}",
                message.TemplateKey,
                (int)response.StatusCode,
                errorDetails);

            throw new HttpRequestException(
                $"Mailjet rejected the transactional email request with HTTP {(int)response.StatusCode}. {errorDetails}",
                null,
                response.StatusCode);
        }

        logger.LogInformation(
            "Mailjet accepted transactional email template {TemplateKey}.",
            message.TemplateKey);
    }

    private void ValidateConfiguration()
    {
        if (!IsConfiguredSecret(mailjetOptions.ApiKey) ||
            !IsConfiguredSecret(mailjetOptions.ApiSecret) ||
            string.IsNullOrWhiteSpace(mailjetOptions.SenderEmail))
        {
            throw new InvalidOperationException(
                "Mailjet API credentials and sender email must be configured.");
        }
    }

    private static bool IsConfiguredSecret(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadErrorDetailsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await response.Content.ReadFromJsonAsync<MailjetSendResponse>(
                cancellationToken);
            var errors = result?.Messages?
                .SelectMany(message => message.Errors ?? [])
                .Select(error => string.Join(
                    " | ",
                    new[]
                    {
                        error.ErrorIdentifier,
                        error.ErrorCode,
                        error.ErrorMessage
                    }.Where(value => !string.IsNullOrWhiteSpace(value))))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            return errors is { Length: > 0 }
                ? string.Join("; ", errors)
                : "Mailjet returned no structured error details.";
        }
        catch (JsonException)
        {
            return "Mailjet returned an unreadable error response.";
        }
    }

    private sealed record MailjetSendResponse(MailjetMessageResponse[]? Messages);

    private sealed record MailjetMessageResponse(
        string? Status,
        MailjetError[]? Errors);

    private sealed record MailjetError(
        string? ErrorIdentifier,
        string? ErrorCode,
        int? StatusCode,
        string? ErrorMessage);
}
