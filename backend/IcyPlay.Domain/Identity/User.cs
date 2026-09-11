using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Identity;

public sealed class User : Entity
{
    private User()
    {
    }
    public User(string email, string fullName, string? phoneNumber)
    {
        Email = email.Trim().ToLowerInvariant();
        FullName = fullName.Trim();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
    }
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? PhoneNumber
    {
        get; private set;
    }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? EmailVerifiedAt
    {
        get; private set;
    }
    public bool IsEmailVerified => EmailVerifiedAt is not null;
    public int FailedLoginAttempts
    {
        get; private set;
    }
    public DateTimeOffset? LockoutEnd
    {
        get; private set;
    }
    public ICollection<UserRole> Roles { get; private set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = [];
    public ICollection<EmailVerificationToken> EmailVerificationTokens { get; private set; } = [];
    public ICollection<PasswordResetToken> PasswordResetTokens { get; private set; } = [];
    public ICollection<AccountInvitationToken> AccountInvitationTokens { get; private set; } = [];

    public void SetPasswordHash(string value) => PasswordHash = value;
    public void MarkEmailVerified(DateTimeOffset now)
    {
        if (EmailVerifiedAt is not null)
        {
            return;
        }

        EmailVerifiedAt = now;
        UpdatedAt = now;
    }
    public void RecordFailedLogin(int maximumAttempts, TimeSpan duration, DateTimeOffset now)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maximumAttempts)
        {
            LockoutEnd = now.Add(duration);
        }

        UpdatedAt = now;
    }
    public void ClearLockout(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        UpdatedAt = now;
    }
    public void RecordSuccessfulLogin(DateTimeOffset now)
    {
        FailedLoginAttempts = 0;
        LockoutEnd = null;
        UpdatedAt = now;
    }
}
