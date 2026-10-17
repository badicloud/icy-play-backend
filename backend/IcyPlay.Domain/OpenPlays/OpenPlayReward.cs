namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// What an open play's best players earn when a session ends: a voucher off
/// their next open play at the venue, or a prize handed over at the venue
/// against a claim stub. One kind per open play. Stored as columns on it.
/// </summary>
public sealed record OpenPlayReward
{
    public const int LongestValidity = 365;
    public const int MostClaimStubs = 500;
    public const int PrizeLimit = 120;
    public const decimal LargestFixedVoucher = 100_000m;

    private OpenPlayReward()
    {
    }

    private OpenPlayReward(
        string kind,
        string? discountKind,
        decimal? discountValue,
        decimal? maxDiscount,
        string? prize,
        int? quantity,
        int validDays)
    {
        if (validDays < 1 || validDays > LongestValidity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validDays), $"A reward is valid for 1 to {LongestValidity} days.");
        }

        Kind = kind;
        DiscountKind = discountKind;
        DiscountValue = discountValue;
        MaxDiscount = maxDiscount;
        Prize = prize;
        Quantity = quantity;
        ValidDays = validDays;
    }

    /// <summary>A peso or percentage voucher off the player's next open play at the venue.</summary>
    public static OpenPlayReward Voucher(string discountKind, decimal discountValue, decimal? maxDiscount, int validDays)
    {
        if (!OpenPlayDiscountKind.IsSupported(discountKind))
        {
            throw new ArgumentException($"'{discountKind}' is not a supported discount.", nameof(discountKind));
        }

        if (discountValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "A voucher must be worth more than zero.");
        }

        if (discountKind == OpenPlayDiscountKind.Percentage && discountValue > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "A voucher cannot be more than 100% off.");
        }

        if (discountKind == OpenPlayDiscountKind.Fixed && discountValue > LargestFixedVoucher)
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "That voucher is worth too much.");
        }

        if (maxDiscount is decimal cap && (cap <= 0 || discountKind != OpenPlayDiscountKind.Percentage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDiscount), "A most-off amount is for a percentage voucher, and more than zero.");
        }

        return new OpenPlayReward(OpenPlayRewardKind.Voucher, discountKind, discountValue, maxDiscount, null, null, validDays);
    }

    /// <summary>A prize at the venue — a drink, a jersey — handed over against a claim stub. So many to give.</summary>
    public static OpenPlayReward ClaimStub(string prize, int quantity, int validDays)
    {
        if (string.IsNullOrWhiteSpace(prize) || prize.Trim().Length > PrizeLimit)
        {
            throw new ArgumentException($"Say what the prize is, in up to {PrizeLimit} characters.", nameof(prize));
        }

        if (quantity < 1 || quantity > MostClaimStubs)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), $"Give 1 to {MostClaimStubs} prizes.");
        }

        return new OpenPlayReward(OpenPlayRewardKind.ClaimStub, null, null, null, prize.Trim(), quantity, validDays);
    }

    /// <summary>One of <see cref="OpenPlayRewardKind"/>.</summary>
    public string Kind { get; private init; } = OpenPlayRewardKind.Voucher;

    /// <summary>A voucher's: "Fixed" or "Percentage".</summary>
    public string? DiscountKind
    {
        get; private init;
    }

    /// <summary>A voucher's: pesos, or per cent.</summary>
    public decimal? DiscountValue
    {
        get; private init;
    }

    /// <summary>A percentage voucher's most it takes off, in pesos; null for no limit.</summary>
    public decimal? MaxDiscount
    {
        get; private init;
    }

    /// <summary>A claim stub's prize, as the players are told it.</summary>
    public string? Prize
    {
        get; private init;
    }

    /// <summary>How many claim stubs there are to give, over all sessions.</summary>
    public int? Quantity
    {
        get; private init;
    }

    /// <summary>Days from when it is given to when it expires.</summary>
    public int ValidDays
    {
        get; private init;
    }
}

public static class OpenPlayRewardKind
{
    /// <summary>Off the player's next open play at the venue, at checkout.</summary>
    public const string Voucher = "Voucher";

    /// <summary>A prize handed over at the venue against a claim stub.</summary>
    public const string ClaimStub = "ClaimStub";
}
