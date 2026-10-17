using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// A best player's voucher: so much off their next open play at the venue
/// that gave it, until it expires. Taken off the venue's fee, early bird or
/// not; the platform fee is untouched.
/// </summary>
public sealed class OpenPlayVoucher : Entity
{
    private OpenPlayVoucher()
    {
    }

    public OpenPlayVoucher(
        Guid facilityId,
        Guid customerUserId,
        Guid openPlayId,
        Guid sessionId,
        OpenPlayReward reward,
        DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(reward);

        if (reward.Kind != OpenPlayRewardKind.Voucher)
        {
            throw new ArgumentException("Only a voucher reward makes a voucher.", nameof(reward));
        }

        FacilityId = facilityId;
        CustomerUserId = customerUserId;
        OpenPlayId = openPlayId;
        SessionId = sessionId;
        DiscountKind = reward.DiscountKind!;
        DiscountValue = reward.DiscountValue!.Value;
        MaxDiscount = reward.MaxDiscount;
        Status = OpenPlayVoucherStatus.Available;
        CreatedAt = issuedAt;
        ExpiresAt = issuedAt.AddDays(reward.ValidDays);
    }

    /// <summary>The venue that gave it, and the only one it is good at.</summary>
    public Guid FacilityId
    {
        get; private set;
    }

    public Guid CustomerUserId
    {
        get; private set;
    }

    /// <summary>Where it was won: the open play and its session.</summary>
    public Guid OpenPlayId
    {
        get; private set;
    }

    public Guid SessionId
    {
        get; private set;
    }

    /// <summary>"Fixed" or "Percentage".</summary>
    public string DiscountKind { get; private set; } = OpenPlayDiscountKind.Fixed;

    /// <summary>Pesos, or per cent.</summary>
    public decimal DiscountValue
    {
        get; private set;
    }

    /// <summary>A percentage voucher's most it takes off; null for no limit.</summary>
    public decimal? MaxDiscount
    {
        get; private set;
    }

    /// <summary>One of <see cref="OpenPlayVoucherStatus"/>.</summary>
    public string Status { get; private set; } = OpenPlayVoucherStatus.Available;

    public DateTimeOffset ExpiresAt
    {
        get; private set;
    }

    /// <summary>The registration it is held for or was used on.</summary>
    public Guid? RegistrationId
    {
        get; private set;
    }

    public DateTimeOffset? UsedAt
    {
        get; private set;
    }

    public DateTimeOffset? RevokedAt
    {
        get; private set;
    }

    public bool IsUsableAt(DateTimeOffset moment) =>
        Status == OpenPlayVoucherStatus.Available && moment < ExpiresAt;

    /// <summary>The pesos it takes off a fee, never more than the fee.</summary>
    public decimal DiscountOn(decimal fee)
    {
        var off = DiscountKind == OpenPlayDiscountKind.Percentage
            ? Math.Round(fee * DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : DiscountValue;

        if (MaxDiscount is decimal cap)
        {
            off = Math.Min(off, cap);
        }

        return Math.Clamp(off, 0m, Math.Max(fee, 0m));
    }

    /// <summary>Held for a registration while it is being paid for.</summary>
    internal void Reserve(Guid registrationId, DateTimeOffset now)
    {
        if (!IsUsableAt(now))
        {
            throw new InvalidOperationException("This voucher cannot be used.");
        }

        Status = OpenPlayVoucherStatus.Reserved;
        RegistrationId = registrationId;
        UpdatedAt = now;
    }

    /// <summary>The registration it was held for went through: it is spent.</summary>
    public void MarkUsed(DateTimeOffset usedAt, DateTimeOffset now)
    {
        if (Status != OpenPlayVoucherStatus.Reserved)
        {
            return;
        }

        Status = OpenPlayVoucherStatus.Used;
        UsedAt = usedAt;
        UpdatedAt = now;
    }

    /// <summary>
    /// The registration it was held for did not go through — not paid in time,
    /// cancelled, turned down — so it is the player's to use again, until it
    /// expires.
    /// </summary>
    public void Release(DateTimeOffset now)
    {
        if (Status != OpenPlayVoucherStatus.Reserved)
        {
            return;
        }

        Status = OpenPlayVoucherStatus.Available;
        RegistrationId = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Taken back when the session it was won in is reopened: the results it
    /// came from no longer stand. Not once it has been used or is being used.
    /// </summary>
    /// <returns>Whether it was taken back.</returns>
    public bool Revoke(DateTimeOffset now)
    {
        if (Status != OpenPlayVoucherStatus.Available)
        {
            return false;
        }

        Status = OpenPlayVoucherStatus.Revoked;
        RevokedAt = now;
        UpdatedAt = now;

        return true;
    }
}

public static class OpenPlayVoucherStatus
{
    public const string Available = "Available";

    /// <summary>Held for a registration while it is being paid for.</summary>
    public const string Reserved = "Reserved";

    public const string Used = "Used";

    /// <summary>Taken back: the session it was won in was reopened.</summary>
    public const string Revoked = "Revoked";
}
