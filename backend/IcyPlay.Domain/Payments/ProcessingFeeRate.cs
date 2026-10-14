namespace IcyPlay.Domain.Payments;

/// <summary>
/// What the gateway takes for one way of paying: a percentage of what is
/// charged, plus a fixed amount, with VAT on top of both — the gateway's
/// published rates exclude it.
///
/// The customer pays this, not the venue. So what is charged is grossed up:
/// the smallest amount that, once the gateway has taken its fee, still leaves
/// exactly what is owed. The fee is worked out the way that costs the most —
/// each part rounded up to the centavo — so a gateway that rounds the other
/// way only ever leaves a centavo over, never one short. Short would flag the
/// payment for the desk; over is lost in the rounding.
/// </summary>
public sealed record ProcessingFeeRate
{
    /// <summary>Philippine VAT, charged on the gateway's fee.</summary>
    public const decimal Vat = 0.12m;

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

    /// <summary>As published: 2.23 for 2.23%.</summary>
    public decimal Percent { get; }

    /// <summary>Pesos added to every payment, before VAT.</summary>
    public decimal FixedAmount { get; }

    /// <summary>The fee, VAT included, on an amount charged — rounded against us.</summary>
    public decimal FeeOn(decimal charged)
    {
        var fee = UpToCentavo((charged * Percent / 100m) + FixedAmount);
        return fee + UpToCentavo(fee * Vat);
    }

    /// <summary>What to charge so that <paramref name="amountDue"/> is what arrives.</summary>
    public decimal GrossUp(decimal amountDue)
    {
        if (amountDue <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amountDue), amountDue, "Only something owed can be charged.");
        }

        const decimal withVat = 1m + Vat;

        // The answer without rounding, which no real total can be below; then
        // up a centavo at a time until rounding leaves enough. One or two steps.
        var charged = UpToCentavo(
            (amountDue + (FixedAmount * withVat)) / (1m - (Percent / 100m * withVat)));

        while (charged - FeeOn(charged) < amountDue)
        {
            charged += 0.01m;
        }

        return charged;
    }

    private static decimal UpToCentavo(decimal amount) => Math.Ceiling(amount * 100m) / 100m;
}
