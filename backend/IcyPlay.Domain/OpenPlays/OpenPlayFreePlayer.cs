using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// A player the venue put into a session for free: a regular, a coach, a
/// member of staff — somebody to make up the numbers when too few signed up
/// to play a full game.
///
/// Its own record rather than an <see cref="OpenPlayRegistration"/> with a
/// price of nothing. A registration is a customer's: it is paid for, has a
/// receipt, emails, a QR and a place in "My open plays", and every one of
/// those would have to learn to leave a free player out. This takes a spot
/// and stands on the roster, and that is all it does.
///
/// Added and removed only by the venue's own desk. The audit trail keeps the
/// name and who added it, so a removed one leaves no row behind.
/// </summary>
public sealed class OpenPlayFreePlayer : Entity
{
    public const int NameLimit = 100;
    public const int MobileLimit = 30;

    private OpenPlayFreePlayer()
    {
    }

    public OpenPlayFreePlayer(
        Guid sessionId,
        string fullName,
        string? mobile,
        Guid addedByUserId,
        DateTimeOffset addedAt)
    {
        var name = fullName?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A free player needs a name.", nameof(fullName));
        }

        if (name.Length > NameLimit)
        {
            throw new ArgumentException($"A name is at most {NameLimit} characters.", nameof(fullName));
        }

        var phone = string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim();

        if (phone is { Length: > MobileLimit })
        {
            throw new ArgumentException($"A mobile number is at most {MobileLimit} characters.", nameof(mobile));
        }

        SessionId = sessionId;
        FullName = name;
        Mobile = phone;
        AddedByUserId = addedByUserId;
        CreatedAt = addedAt;
    }

    public Guid SessionId
    {
        get; private set;
    }
    public OpenPlaySession Session { get; private set; } = null!;

    public string FullName { get; private set; } = string.Empty;

    /// <summary>Optional: for the desk to reach them, never shown anywhere public.</summary>
    public string? Mobile
    {
        get; private set;
    }

    /// <summary>The attendant or owner who put them in.</summary>
    public Guid AddedByUserId
    {
        get; private set;
    }

    public DateTimeOffset? CheckedInAt
    {
        get; private set;
    }

    public Guid? CheckedInByUserId
    {
        get; private set;
    }

    public bool IsCheckedIn => CheckedInAt is not null;

    public void CheckIn(Guid checkedInByUserId, DateTimeOffset now)
    {
        if (IsCheckedIn)
        {
            return;
        }

        CheckedInAt = now;
        CheckedInByUserId = checkedInByUserId;
        UpdatedAt = now;
    }

    public void UndoCheckIn(DateTimeOffset now)
    {
        if (!IsCheckedIn)
        {
            return;
        }

        CheckedInAt = null;
        CheckedInByUserId = null;
        UpdatedAt = now;
    }
}
