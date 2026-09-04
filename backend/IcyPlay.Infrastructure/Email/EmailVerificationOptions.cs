namespace IcyPlay.Infrastructure.Email;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public string VerificationUrl { get; init; } = string.Empty;
    public int ExpirationHours { get; init; } = 24;
    public int ResendCooldownSeconds { get; init; } = 60;
    public string SupportEmail { get; init; } = string.Empty;
}
