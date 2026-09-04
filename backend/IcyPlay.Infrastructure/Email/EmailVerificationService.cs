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

public sealed class EmailVerificationService(
    AppDbContext dbContext,
    ITransactionalEmailSender emailSender,
    IOptions<EmailVerificationOptions> options,
    TimeProvider timeProvider,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    private readonly EmailVerificationOptions verificationOptions = options.Value;

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
        var expiresAt = now.AddHours(verificationOptions.ExpirationHours);
        var verificationUrl = QueryHelpers.AddQueryString(
            verificationOptions.VerificationUrl,
            "token",
            rawToken);

        dbContext.EmailVerificationTokens.Add(new EmailVerificationToken(
            userId,
            tokenHash,
            expiresAt,
            now));
        await dbContext.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.AccountVerification,
                recipientEmail,
                recipientName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = recipientName,
                    ["verification_url"] = verificationUrl,
                    ["expiration_hours"] = verificationOptions.ExpirationHours,
                    ["support_email"] = verificationOptions.SupportEmail,
                    ["current_year"] = now.Year
                }),
            cancellationToken);

        logger.LogInformation(
            "Account verification email was accepted for user {UserId}.",
            userId);
    }

    private void ValidateConfiguration()
    {
        if (!Uri.TryCreate(
                verificationOptions.VerificationUrl,
                UriKind.Absolute,
                out _) ||
            verificationOptions.ExpirationHours <= 0 ||
            verificationOptions.ResendCooldownSeconds <= 0 ||
            string.IsNullOrWhiteSpace(verificationOptions.SupportEmail))
        {
            throw new InvalidOperationException(
                "Email verification URL, expiration, and support email must be configured.");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
