using System.Security.Cryptography;
using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// One prize an open play has to give, as a claim stub: made when the venue
/// says how many prizes there are, given to a best player when a session
/// ends, and marked claimed by the venue when the player shows it.
/// </summary>
public sealed class OpenPlayClaimStub : Entity
{
    private OpenPlayClaimStub()
    {
    }

    public OpenPlayClaimStub(Guid openPlayId, Guid facilityId, string code, string prize, DateTimeOffset createdAt)
    {
        OpenPlayId = openPlayId;
        FacilityId = facilityId;
        Code = code;
        Prize = prize;
        CreatedAt = createdAt;
    }

    public Guid OpenPlayId
    {
        get; private set;
    }

    public Guid FacilityId
    {
        get; private set;
    }

    /// <summary>"7KQ2-M9XA": what the player shows, and the desk types or scans.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What it is for, as the venue said it. Fixed once the stub is given.</summary>
    public string Prize { get; private set; } = string.Empty;

    /// <summary>The best player it was given to.</summary>
    public Guid? CustomerUserId
    {
        get; private set;
    }

    /// <summary>The session it was won in.</summary>
    public Guid? SessionId
    {
        get; private set;
    }

    public DateTimeOffset? AssignedAt
    {
        get; private set;
    }

    public DateTimeOffset? ExpiresAt
    {
        get; private set;
    }

    public DateTimeOffset? ClaimedAt
    {
        get; private set;
    }

    /// <summary>Who at the venue marked it claimed.</summary>
    public Guid? ClaimedByUserId
    {
        get; private set;
    }

    public bool IsAssigned => CustomerUserId is not null;

    public bool IsClaimed => ClaimedAt is not null;

    public string StatusAt(DateTimeOffset moment) =>
        IsClaimed ? OpenPlayClaimStubStatus.Claimed
        : !IsAssigned ? OpenPlayClaimStubStatus.Available
        : moment >= ExpiresAt ? OpenPlayClaimStubStatus.Expired
        : OpenPlayClaimStubStatus.Given;

    /// <summary>A prize not yet given takes the venue's new wording.</summary>
    public void Reword(string prize, DateTimeOffset now)
    {
        if (IsAssigned || prize == Prize)
        {
            return;
        }

        Prize = prize;
        UpdatedAt = now;
    }

    public void Assign(Guid customerUserId, Guid sessionId, int validDays, DateTimeOffset now)
    {
        if (IsAssigned)
        {
            throw new InvalidOperationException("This claim stub has already been given.");
        }

        CustomerUserId = customerUserId;
        SessionId = sessionId;
        AssignedAt = now;
        ExpiresAt = now.AddDays(validDays);
        UpdatedAt = now;
    }

    /// <summary>Back to the prizes still to give: the session it was won in was reopened. Not once claimed.</summary>
    public bool Unassign(DateTimeOffset now)
    {
        if (!IsAssigned || IsClaimed)
        {
            return false;
        }

        CustomerUserId = null;
        SessionId = null;
        AssignedAt = null;
        ExpiresAt = null;
        UpdatedAt = now;

        return true;
    }

    public void Claim(Guid claimedByUserId, DateTimeOffset now)
    {
        ClaimedAt = now;
        ClaimedByUserId = claimedByUserId;
        UpdatedAt = now;
    }

    /// <summary>
    /// A fresh code: eight characters from an alphabet without look-alikes
    /// (no 0/O, 1/I/L), as two groups of four.
    /// </summary>
    public static string NewCode()
    {
        const string alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        Span<char> code = stackalloc char[9];

        for (var index = 0; index < code.Length; index++)
        {
            code[index] = index == 4 ? '-' : alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(code);
    }
}

public static class OpenPlayClaimStubStatus
{
    /// <summary>Not given to anybody yet.</summary>
    public const string Available = "Available";

    /// <summary>Given to a best player, waiting to be claimed.</summary>
    public const string Given = "Given";

    public const string Claimed = "Claimed";

    /// <summary>Given, but not claimed in time.</summary>
    public const string Expired = "Expired";
}
