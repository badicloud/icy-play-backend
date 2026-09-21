namespace IcyPlay.Infrastructure.Email;

/// <summary>
/// Where the new-device alert sends somebody who did not recognise the sign-in.
/// </summary>
public sealed class SignInAlertOptions
{
    public const string SectionName = "SignInAlerts";

    /// <summary>
    /// The page that asks for an email address and sends a reset link.
    ///
    /// Not a reset link itself. Minting a token for every new-device sign-in
    /// would put a live password-reset key in an inbox nobody asked to have
    /// one in, and the person who needs it can ask for it themselves.
    /// </summary>
    public string ForgotPasswordUrl { get; init; } = string.Empty;

    public string SupportEmail { get; init; } = string.Empty;
}
