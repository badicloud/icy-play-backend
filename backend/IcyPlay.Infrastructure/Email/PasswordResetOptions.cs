namespace IcyPlay.Infrastructure.Email;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public string ResetUrl { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 60;
    public int RequestCooldownSeconds { get; init; } = 60;
    public string SupportEmail { get; init; } = string.Empty;
}
