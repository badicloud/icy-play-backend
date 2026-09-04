using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Identity;

public sealed class EmailVerificationToken : Entity
{
    private EmailVerificationToken()
    {
    }

    public EmailVerificationToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt)
        : this(userId, tokenHash, expiresAt, DateTimeOffset.UtcNow)
    {
    }

    public EmailVerificationToken(
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
    public DateTimeOffset? UsedAt
    {
        get; private set;
    }

    public bool IsActive(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now)
    {
        UsedAt = now;
        UpdatedAt = now;
    }
}
