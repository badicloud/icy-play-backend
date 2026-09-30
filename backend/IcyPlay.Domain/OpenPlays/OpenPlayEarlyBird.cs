namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// A lower price for players who register early enough before the session.
/// Whoever creates the open play picks a fixed peso amount or a percentage.
///
/// Relative to each session's start, like the cut-off, so one setting works
/// for every date of the series. Stored as columns on the open play.
/// </summary>
public sealed record OpenPlayEarlyBird
{
    private OpenPlayEarlyBird()
    {
    }

    public OpenPlayEarlyBird(string discountKind, decimal discountValue, int leadMinutes)
    {
        if (!OpenPlayDiscountKind.IsSupported(discountKind))
        {
            throw new ArgumentException($"'{discountKind}' is not a supported discount.", nameof(discountKind));
        }

        if (discountValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "An early-bird discount must be more than zero.");
        }

        if (discountKind == OpenPlayDiscountKind.Percentage && discountValue > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(discountValue), "A discount cannot be more than 100%.");
        }

        if (leadMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leadMinutes), "The early-bird deadline must be before the start.");
        }

        DiscountKind = discountKind;
        DiscountValue = discountValue;
        LeadMinutes = leadMinutes;
    }

    /// <summary>One of <see cref="OpenPlayDiscountKind"/>.</summary>
    public string DiscountKind { get; private init; } = OpenPlayDiscountKind.Fixed;

    /// <summary>Pesos for a fixed discount, per cent for a percentage.</summary>
    public decimal DiscountValue
    {
        get; private init;
    }

    /// <summary>How long before the session starts a player must register to get the discount.</summary>
    public int LeadMinutes
    {
        get; private init;
    }

    /// <summary>The pesos taken off this fee, never more than the fee itself.</summary>
    public decimal DiscountOn(decimal registrationFee)
    {
        var discount = DiscountKind == OpenPlayDiscountKind.Percentage
            ? Math.Round(registrationFee * DiscountValue / 100m, 2, MidpointRounding.AwayFromZero)
            : DiscountValue;

        return Math.Min(discount, registrationFee);
    }

    internal void EnsureFits(decimal registrationFee, int registrationCutoffMinutes)
    {
        if (DiscountKind == OpenPlayDiscountKind.Fixed && DiscountValue > registrationFee)
        {
            throw new ArgumentException("A fixed discount cannot be more than the registration fee.");
        }

        // An early-bird deadline after registration closes can never be met.
        if (LeadMinutes <= registrationCutoffMinutes)
        {
            throw new ArgumentException("The early-bird deadline must come before registration closes.");
        }
    }
}

public static class OpenPlayDiscountKind
{
    public const string Fixed = "Fixed";
    public const string Percentage = "Percentage";

    public static readonly IReadOnlyCollection<string> All = [Fixed, Percentage];

    public static bool IsSupported(string value) => All.Contains(value);
}

/// <summary>
/// One player's price for one session, as quoted at a moment. The registration
/// keeps a copy, so changing the open play later cannot change what somebody
/// already agreed to pay.
/// </summary>
public readonly record struct OpenPlayPrice(decimal RegistrationFee, decimal Discount, decimal PlatformFee)
{
    public decimal Total => RegistrationFee - Discount + PlatformFee;
}
