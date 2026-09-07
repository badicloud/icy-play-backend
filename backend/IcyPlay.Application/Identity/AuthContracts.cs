namespace IcyPlay.Application.Identity;

public sealed record RegisterCustomerRequest(string FullName, string Email, string Password, string? PhoneNumber, bool AcceptedTerms, string CaptchaToken);
public sealed record LoginRequest(string Email, string Password, string CaptchaToken, bool RememberMe);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ResendVerificationEmailRequest(string Email, string CaptchaToken);
public sealed record VerifyEmailRequest(string Token);
public sealed record ForgotPasswordRequest(string Email, string CaptchaToken);
public sealed record ResetPasswordRequest(string Token, string NewPassword);
public sealed record CheckPasswordResetTokenRequest(string Token);
/// <summary>Request-scoped client details, collected by the API layer.</summary>
public sealed record ClientInfo(string? UserAgent, string? IpAddress)
{
    public static readonly ClientInfo Unknown = new(null, null);
}
public sealed record RegistrationResponse(Guid UserId, Guid ProfileId);
public sealed record ResendVerificationEmailResponse(string Message);
public sealed record VerifyEmailResponse(string Email, DateTimeOffset VerifiedAt, bool AlreadyVerified);
public sealed record ForgotPasswordResponse(string Message);
public sealed record ResetPasswordResponse(string Email, int RevokedSessions);
public sealed record PasswordResetTokenStatusResponse(DateTimeOffset ExpiresAt);
public sealed record ActiveSessionResponse(
    Guid Id,
    string? UserAgent,
    string? IpAddress,
    DateTimeOffset SignedInAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsPersistent,
    bool IsCurrent);
public sealed record RevokeOtherSessionsResponse(int RevokedSessions);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, IReadOnlyCollection<string> Roles);
public sealed record CurrentUserResponse(Guid UserId, string Email, string FullName, IReadOnlyCollection<string> Roles);
public enum AuthFailure
{
    None, DuplicateEmail, InvalidCredentials, AccountLocked, InvalidRefreshToken, InactiveAccount, VerificationCooldown, InvalidVerificationToken, ExpiredVerificationToken, PasswordResetCooldown, InvalidPasswordResetToken, ExpiredPasswordResetToken, PasswordReused
}
public sealed record AuthResult<T>(T? Value, AuthFailure Failure = AuthFailure.None, int? RetryAfterSeconds = null)
{
    public bool Succeeded => Failure == AuthFailure.None;
    public static AuthResult<T> Success(T value) => new(value);
    public static AuthResult<T> Fail(AuthFailure failure, int? retryAfterSeconds = null) => new(default, failure, retryAfterSeconds);
}
