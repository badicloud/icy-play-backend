namespace IcyPlay.Api.Common;

/// <summary>
/// reCAPTCHA v3 action names. Each value must match the action the browser
/// passes to grecaptcha.execute, because the verifier rejects a mismatch.
/// </summary>
public static class CaptchaAction
{
    public const string Register = "register";
    public const string Login = "login";
    public const string ForgotPassword = "forgot_password";
    public const string ResendVerification = "resend_verification";
}
