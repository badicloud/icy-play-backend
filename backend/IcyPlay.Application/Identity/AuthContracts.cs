namespace IcyPlay.Application.Identity;

public sealed record RegisterCustomerRequest(string FullName, string Email, string Password, string? PhoneNumber, bool AcceptedTerms, string CaptchaToken);
public sealed record RegisterFacilityOwnerRequest(string FullName, string Email, string Password, string BusinessName, string BillingEmail, string? BillingPhone, bool AcceptedTerms, string CaptchaToken);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record ResendVerificationEmailRequest(string Email);
public sealed record VerifyEmailRequest(string Token);
public sealed record RegistrationResponse(Guid UserId, Guid ProfileId);
public sealed record ResendVerificationEmailResponse(string Message);
public sealed record VerifyEmailResponse(string Email, DateTimeOffset VerifiedAt, bool AlreadyVerified);
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, IReadOnlyCollection<string> Roles);
public sealed record CurrentUserResponse(Guid UserId, string Email, string FullName, IReadOnlyCollection<string> Roles);
public enum AuthFailure
{
    None, DuplicateEmail, InvalidCredentials, AccountLocked, InvalidRefreshToken, InactiveAccount, VerificationCooldown, InvalidVerificationToken, ExpiredVerificationToken
}
public sealed record AuthResult<T>(T? Value, AuthFailure Failure = AuthFailure.None, int? RetryAfterSeconds = null)
{
    public bool Succeeded => Failure == AuthFailure.None;
    public static AuthResult<T> Success(T value) => new(value);
    public static AuthResult<T> Fail(AuthFailure failure, int? retryAfterSeconds = null) => new(default, failure, retryAfterSeconds);
}
