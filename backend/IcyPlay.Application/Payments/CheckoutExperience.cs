namespace IcyPlay.Application.Payments;

/// <summary>
/// What paying online looks like on the second step, for venues paid
/// <see cref="Domain.Payments.PaymentMode.Direct"/>. A server setting, not a
/// venue's: it changes the page, never who is paid or how much. Venues paid
/// <see cref="Domain.Payments.PaymentMode.Manual"/> never see either.
///
/// Strings rather than an enum, matching <see cref="Domain.Payments.PaymentMode"/>.
/// </summary>
public static class CheckoutExperience
{
    /// <summary>
    /// The customer is sent to the gateway's own checkout page and picks how
    /// to pay there. The default: it is the one that has been paid through.
    /// </summary>
    public const string Hosted = "Hosted";

    /// <summary>
    /// The customer picks how to pay on our page, sees each method's total,
    /// scans a QR Ph code without leaving, and goes straight to GCash or Maya
    /// to approve — coming back to us whatever happens there.
    /// </summary>
    public const string Integrated = "Integrated";

    public static readonly IReadOnlyCollection<string> All = [Hosted, Integrated];

    /// <summary>
    /// The setting as given, or <see cref="Hosted"/> when it is missing or
    /// misspelt: a typo in configuration should not change the checkout.
    /// </summary>
    public static string From(string? value) =>
        All.FirstOrDefault(known => string.Equals(known, value?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Hosted;
}
