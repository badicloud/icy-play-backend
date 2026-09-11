namespace IcyPlay.Infrastructure.Email;

public sealed class AccountInvitationOptions
{
    public const string SectionName = "AccountInvitation";

    /// <summary>Frontend page the emailed link points at.</summary>
    public string ActivationUrl { get; init; } = string.Empty;

    /// <summary>
    /// Days, not minutes. Onboarding is sales-led, so an owner may not open
    /// their email the same afternoon the platform team encodes them.
    /// </summary>
    public int ExpirationDays { get; init; } = 7;

    public string SupportEmail { get; init; } = string.Empty;
}
