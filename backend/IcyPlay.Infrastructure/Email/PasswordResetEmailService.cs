using System.Security.Cryptography;
using System.Text;
using IcyPlay.Application.Email;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Email;

public sealed class PasswordResetEmailService(
    AppDbContext dbContext,
    ITransactionalEmailSender emailSender,
    IOptions<PasswordResetOptions> options,
    TimeProvider timeProvider,
    ILogger<PasswordResetEmailService> logger) : IPasswordResetEmailService
{
    private readonly PasswordResetOptions resetOptions = options.Value;

    public async Task SendAsync(
        Guid userId,
        string recipientEmail,
        string recipientName,
        CancellationToken cancellationToken)
    {
        ValidateConfiguration();

        var now = timeProvider.GetUtcNow();
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Hash(rawToken);
        var expiresAt = now.AddMinutes(resetOptions.ExpirationMinutes);
        var resetUrl = QueryHelpers.AddQueryString(
            resetOptions.ResetUrl,
            "token",
            rawToken);

        dbContext.PasswordResetTokens.Add(new PasswordResetToken(
            userId,
            tokenHash,
            expiresAt,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.PasswordReset,
                recipientEmail,
                recipientName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = recipientName,
                    ["reset_url"] = resetUrl,
                    ["expiration_minutes"] = resetOptions.ExpirationMinutes,
                    ["support_email"] = resetOptions.SupportEmail,
                    ["current_year"] = now.Year
                }),
            cancellationToken);

        logger.LogInformation(
            "Password reset email was accepted for user {UserId}.",
            userId);
    }

    private void ValidateConfiguration()
    {
        if (!Uri.TryCreate(
                resetOptions.ResetUrl,
                UriKind.Absolute,
                out _) ||
            resetOptions.ExpirationMinutes <= 0 ||
            resetOptions.RequestCooldownSeconds <= 0 ||
            string.IsNullOrWhiteSpace(resetOptions.SupportEmail))
        {
            throw new InvalidOperationException(
                "Password reset URL, expiration, and support email must be configured.");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
