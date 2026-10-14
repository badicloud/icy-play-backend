namespace IcyPlay.Domain.Payments;

/// <summary>
/// What the gateway takes for one way of paying, as it actually charges it: a
/// single percentage of what is charged, VAT already inside it, plus any fixed
/// amount.
///
/// PayMongo publishes its rates without VAT (GCash 2.23%), but what it takes
/// from a payment is that rate with VAT added and rounded to a round figure —
/// 2.5% for GCash, 2.0% for Maya, 1.5% for QR Ph — times what was charged,
/// rounded to the centavo. Working VAT out separately came to 2.4976% for
/// GCash, a hair under, and left a payment a centavo short. So the rate here
/// is the one the gateway takes, never the one it publishes.
///
/// The customer pays this, not the venue. So what is charged is grossed up:
/// the smallest amount that, once the gateway has taken its fee, still leaves
/// what is owed. The fee is rounded up to the centavo, where the gateway
/// rounds to the nearest, so at worst a centavo is left over — never one
/// short. Short would flag the payment for the desk.
/// </summary>
public sealed record ProcessingFeeRate
{
    public ProcessingFeeRate(decimal percent, decimal fixedAmount = 0m)
    {
        if (percent is < 0m or >= 50m)
        {
            throw new ArgumentOutOfRangeException(nameof(percent), percent, "A fee rate is a percentage from 0 to under 50.");
        }

        if (fixedAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedAmount), fixedAmount, "A fixed fee cannot be negative.");
        }

        Percent = percent;
        FixedAmount = fixedAmount;
    }

    /// <summary>As the gateway takes it, VAT included: 2.5 for 2.5%.</summary>
    public decimal Percent { get; }

    /// <summary>Pesos added to every payment, VAT included.</summary>
    public decimal FixedAmount { get; }

    /// <summary>The fee on an amount charged, rounded against us.</summary>
    public decimal FeeOn(decimal charged) => UpToCentavo((charged * Percent / 100m) + FixedAmount);

    /// <summary>What to charge so that <paramref name="amountDue"/> is what arrives.</summary>
    public decimal GrossUp(decimal amountDue)
    {
        if (amountDue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amountDue), amountDue, "Only something owed can be charged.");
        }

        // The answer without rounding, which no real total can be below; then
        // up a centavo at a time until rounding leaves enough. One step at most.
        var charged = UpToCentavo((amountDue + FixedAmount) / (1m - (Percent / 100m)));

        while (charged - FeeOn(charged) < amountDue)
        {
            charged += 0.01m;
        }

        return charged;
    }

    private static decimal UpToCentavo(decimal amount) => Math.Ceiling(amount * 100m) / 100m;
}
