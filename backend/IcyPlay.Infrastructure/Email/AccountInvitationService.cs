using System.Security.Cryptography;
using System.Text;
using IcyPlay.Application.Email;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Email;

public sealed class AccountInvitationService(
    AppDbContext db,
    ITransactionalEmailSender emailSender,
    IPasswordHasher<User> passwordHasher,
    IOptions<AccountInvitationOptions> options,
    TimeProvider timeProvider,
    ILogger<AccountInvitationService> logger) : IAccountInvitationService
{
    private readonly AccountInvitationOptions invitationOptions = options.Value;

    public async Task SendAsync(
        Guid userId,
        string recipientEmail,
        string recipientName,
        string businessName,
        CancellationToken ct)
    {
        ValidateConfiguration();

        var now = timeProvider.GetUtcNow();

        // Resending must not leave the previous link working. Two live
        // invitations to one account is one more than anybody needs.
        await db.AccountInvitationTokens
            .Where(token => token.UserId == userId && token.AcceptedAt == null && token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAt, now), ct);

        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now.AddDays(invitationOptions.ExpirationDays);

        db.AccountInvitationTokens.Add(new AccountInvitationToken(
            userId,
            Hash(rawToken),
            expiresAt,
            now));
        await db.SaveChangesAsync(ct);

        await emailSender.SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.FacilityOwnerInvitation,
                recipientEmail,
                recipientName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = recipientName,
                    ["business_name"] = businessName,
                    ["activation_url"] = QueryHelpers.AddQueryString(
                        invitationOptions.ActivationUrl,
                        "token",
                        rawToken),
                    ["expiration_days"] = invitationOptions.ExpirationDays,
                    ["support_email"] = invitationOptions.SupportEmail,
                    ["current_year"] = now.Year
                }),
            ct);

        logger.LogInformation("Facility owner invitation was accepted for sending. UserId: {UserId}", userId);
    }

    public async Task<InvitationDetails?> CheckAsync(string rawToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var hash = Hash(rawToken);

        return await db.AccountInvitationTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == hash && token.AcceptedAt == null && token.ExpiresAt > now)
            .Select(token => new InvitationDetails(
                token.User.FullName,
                token.User.Email,
                token.User.PhoneNumber,
                // The business is what tells the recipient which of their
                // venues this account is for.
                db.FacilityOwners
                    .Where(owner => owner.UserId == token.UserId)
                    .Select(owner => owner.BusinessName)
                    .FirstOrDefault(),
                token.ExpiresAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<InvitationAcceptance> AcceptAsync(
        string rawToken,
        string password,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return InvitationAcceptance.InvalidToken;
        }

        var now = timeProvider.GetUtcNow();
        var hash = Hash(rawToken);

        var invitation = await db.AccountInvitationTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, ct);

        if (invitation is null)
        {
            return InvitationAcceptance.InvalidToken;
        }

        // Told apart so the page can say "already used" rather than the flat
        // "invalid", which sends people hunting for a typo that is not there.
        if (invitation.AcceptedAt is not null)
        {
            return InvitationAcceptance.AlreadyAccepted;
        }

        if (now >= invitation.ExpiresAt)
        {
            return InvitationAcceptance.ExpiredToken;
        }

        var user = invitation.User;
        user.SetPasswordHash(passwordHasher.HashPassword(user, password));

        // Opening a link sent to an address proves control of that address, so
        // there is nothing left for a separate verification email to establish.
        user.MarkEmailVerified(now);
        user.ClearLockout(now);
        invitation.Accept(now);

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Facility owner invitation was accepted. UserId: {UserId}", user.Id);
        return InvitationAcceptance.Accepted;
    }

    private void ValidateConfiguration()
    {
        if (!Uri.TryCreate(invitationOptions.ActivationUrl, UriKind.Absolute, out _) ||
            invitationOptions.ExpirationDays <= 0 ||
            string.IsNullOrWhiteSpace(invitationOptions.SupportEmail))
        {
            throw new InvalidOperationException(
                "Account invitation URL, expiration, and support email must be configured.");
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
