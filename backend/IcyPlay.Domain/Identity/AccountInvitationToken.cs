using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Identity;

/// <summary>
/// A single-use invitation for an account somebody else created. Its own kind
/// of token rather than a password reset: an invitation lives for days instead
/// of an hour, lands on its own page, and means "take ownership of this
/// account" rather than "you forgot your password".
///
/// Only the SHA-256 hash is stored. The raw value exists in the emailed link
/// and nowhere else, so a leaked database cannot be used to claim an account.
/// </summary>
public sealed class AccountInvitationToken : Entity
{
    private AccountInvitationToken()
    {
    }

    public AccountInvitationToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset issuedAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = issuedAt;
    }

    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt
    {
        get; private set;
    }
    public DateTimeOffset? AcceptedAt
    {
        get; private set;
    }

    public bool IsUsable(DateTimeOffset now) => AcceptedAt is null && now < ExpiresAt;

    public void Accept(DateTimeOffset now)
    {
        AcceptedAt = now;
        UpdatedAt = now;
    }
}
