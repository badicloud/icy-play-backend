namespace IcyPlay.Api.Common;

public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "AUTH_UNAUTHORIZED";
    public const string InvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AccountLocked = "AUTH_ACCOUNT_LOCKED";
    public const string InvalidRefreshToken = "AUTH_INVALID_REFRESH_TOKEN";
    public const string EmailAlreadyExists = "AUTH_EMAIL_ALREADY_EXISTS";
    public const string AccountInactive = "AUTH_ACCOUNT_INACTIVE";
    public const string Forbidden = "AUTH_FORBIDDEN";
    public const string NotFound = "RESOURCE_NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
    public const string CaptchaInvalid = "CAPTCHA_INVALID";
    public const string VerificationEmailCooldown = "AUTH_VERIFICATION_EMAIL_COOLDOWN";
    public const string InvalidVerificationToken = "AUTH_INVALID_VERIFICATION_TOKEN";
    public const string ExpiredVerificationToken = "AUTH_EXPIRED_VERIFICATION_TOKEN";
    public const string PasswordResetCooldown = "AUTH_PASSWORD_RESET_COOLDOWN";
    public const string InvalidPasswordResetToken = "AUTH_INVALID_PASSWORD_RESET_TOKEN";
    public const string ExpiredPasswordResetToken = "AUTH_EXPIRED_PASSWORD_RESET_TOKEN";
    public const string PasswordReused = "AUTH_PASSWORD_REUSED";
    public const string UnexpectedError = "UNEXPECTED_ERROR";
}
