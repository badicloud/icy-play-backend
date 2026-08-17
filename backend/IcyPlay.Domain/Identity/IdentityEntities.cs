using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Identity;

public sealed class UserRole : Entity
{
    private UserRole()
    {
    }
    public UserRole(Guid userId, string role)
    {
        UserId = userId;
        Role = role;
    }
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
    public string Role { get; private set; } = string.Empty;
}
public sealed class Customer : Entity
{
    private Customer()
    {
    }
    public Customer(Guid userId) => UserId = userId;
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
}
public sealed class FacilityOwner : Entity
{
    private FacilityOwner()
    {
    }
    public FacilityOwner(Guid userId, string businessName, string billingEmail, string? billingPhone)
    {
        UserId = userId;
        BusinessName = businessName.Trim();
        BillingEmail = billingEmail.Trim().ToLowerInvariant();
        BillingPhone = string.IsNullOrWhiteSpace(billingPhone) ? null : billingPhone.Trim();
    }
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
    public string BusinessName { get; private set; } = string.Empty;
    public string BillingEmail { get; private set; } = string.Empty;
    public string? BillingPhone
    {
        get; private set;
    }
    public bool IsActive { get; private set; } = true;
}
public sealed class PlatformAdmin : Entity
{
    private PlatformAdmin()
    {
    }
    public PlatformAdmin(Guid userId) => UserId = userId;
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
}
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }
    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
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
    public DateTimeOffset? RevokedAt
    {
        get; private set;
    }
    public string? ReplacedByTokenHash
    {
        get; private set;
    }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void Revoke(DateTimeOffset now, string? replacementHash = null)
    {
        RevokedAt = now;
        ReplacedByTokenHash = replacementHash;
        UpdatedAt = now;
    }
}
