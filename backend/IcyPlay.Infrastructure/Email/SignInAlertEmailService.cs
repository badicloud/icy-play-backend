using IcyPlay.Application.Email;
using IcyPlay.Domain.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Email;

public sealed class SignInAlertEmailService(
    AppDbContext db,
    ITransactionalEmailSender emailSender,
    IOptions<SignInAlertOptions> options,
    TimeProvider timeProvider,
    ILogger<SignInAlertEmailService> logger) : ISignInAlertEmailService
{
    private SignInAlertOptions Settings => options.Value;

    public async Task SendIfNewDeviceAsync(
        Guid userId,
        string recipientEmail,
        string recipientName,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct)
    {
        if (recipientEmail.Length == 0)
        {
            return;
        }

        try
        {
            if (!await IsNewDeviceAsync(userId, userAgent, ct))
            {
                return;
            }

            await emailSender.SendAsync(
                new TransactionalEmailMessage(
                    EmailTemplateKey.AccountNewSignIn,
                    recipientEmail,
                    recipientName,
                    new Dictionary<string, object>
                    {
                        ["recipient_name"] = recipientName,
                        ["device"] = Describe(userAgent),
                        ["ip_address"] = ipAddress is null or "" ? "an unknown address" : ipAddress,
                        // Written out in full rather than left to the reader's
                        // locale: this is the one fact they check it against,
                        // and "21/09/2026" means two different days depending
                        // on where you are.
                        ["signed_in_at"] = timeProvider
                            .GetUtcNow()
                            .ToString("d MMM yyyy, HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
                        ["forgot_password_url"] = Settings.ForgotPasswordUrl,
                        ["support_email"] = Settings.SupportEmail,
                        ["current_year"] = timeProvider.GetUtcNow().Year
                    }),
                ct);

            logger.LogInformation("A new-device sign-in alert was sent for user {UserId}.", userId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The sign-in has already happened and is not being undone. A
            // missing letter is worse than nothing but far better than turning
            // somebody away from their own account.
            logger.LogError(
                exception,
                "Could not send the new-device sign-in alert for user {UserId}. They are signed in.",
                userId);
        }
    }

    /// <summary>
    /// Whether this account has been signed in from this device before.
    ///
    /// Matched on the user agent alone, not the address. A phone moves between
    /// mobile data and half a dozen networks in a week, so matching on the
    /// address would call the same handset new every other day — which is the
    /// daily letter this is written to avoid.
    ///
    /// Revoked and expired sessions count. The question is "has this person
    /// used this device before", and signing out does not make a laptop
    /// unfamiliar.
    /// </summary>
    private async Task<bool> IsNewDeviceAsync(Guid userId, string? userAgent, CancellationToken ct)
    {
        // Nothing to compare. Treated as new, because the alternative is
        // staying quiet about a sign-in we cannot account for.
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return true;
        }

        // Two, not one: the session being minted right now is already saved by
        // the time this runs, so "seen before" means seen other than this.
        var seen = await db.RefreshTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId && token.UserAgent == userAgent)
            .Take(2)
            .CountAsync(ct);

        return seen < 2;
    }

    /// <summary>
    /// A user agent as a person would say it.
    ///
    /// Not parsed properly — that needs a library and a maintained database of
    /// strings nobody here is going to keep current. This picks the two facts
    /// a reader checks against ("was I on Chrome, on my Mac?") and falls back
    /// to the raw string rather than guessing wrong.
    /// </summary>
    private static string Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return "An unrecognised device";
        }

        var browser =
            userAgent.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : userAgent.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
            : userAgent.Contains("Firefox", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : userAgent.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : userAgent.Contains("Safari", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : null;

        var platform =
            userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone"
            : userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad"
            : userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "Mac"
            : userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : null;

        return (browser, platform) switch
        {
            (not null, not null) => $"{browser} on {platform}",
            (not null, null) => browser,
            (null, not null) => platform,
            // Something we do not recognise at all. Shown as it came rather
            // than as "unknown": a reader who does not recognise it either has
            // learned the thing that matters.
            _ => userAgent.Length > 80 ? userAgent[..80] : userAgent
        };
    }
}
