using System.Security.Claims;
using FluentValidation;
using IcyPlay.Api.Common;
using IcyPlay.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IcyPlay.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    IAuthService authService,
    IRecaptchaVerifier recaptchaVerifier,
    IServiceProvider services,
    ILogger<AuthController> logger) : ControllerBase
{
    [AllowAnonymous, HttpPost("register/customer"), EnableRateLimiting("auth")]
    public async Task<IActionResult> RegisterCustomer(RegisterCustomerRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!await VerifyCaptchaAsync(request.CaptchaToken, CaptchaAction.Register, ct))
        {
            return InvalidCaptcha();
        }

        var result = await authService.RegisterCustomerAsync(request, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new ApiEnvelope<RegistrationResponse>(result.Value)) : Failure(result);
    }

    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!await VerifyCaptchaAsync(request.CaptchaToken, CaptchaAction.Login, ct))
        {
            return InvalidCaptcha();
        }

        var result = await authService.LoginAsync(request, CurrentClient(), ct);
        return result.Succeeded ? Ok(new ApiEnvelope<TokenResponse>(result.Value)) : Failure(result);
    }

    [AllowAnonymous, HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await authService.RefreshAsync(request.RefreshToken, CurrentClient(), ct);
        return result.Succeeded ? Ok(new ApiEnvelope<TokenResponse>(result.Value)) : Failure(result);
    }

    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await authService.LogoutAsync(request.RefreshToken, ct);
        return result.Succeeded ? NoContent() : Failure(result);
    }

    [AllowAnonymous, HttpPost("resend-verification"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendVerificationEmail(
        ResendVerificationEmailRequest request,
        CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!await VerifyCaptchaAsync(request.CaptchaToken, CaptchaAction.ResendVerification, ct))
        {
            return InvalidCaptcha();
        }

        var result = await authService.ResendVerificationEmailAsync(request.Email, ct);
        return result.Succeeded
            ? Accepted(new ApiEnvelope<ResendVerificationEmailResponse>(result.Value))
            : Failure(result);
    }

    [AllowAnonymous, HttpPost("verify-email"), EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await authService.VerifyEmailAsync(request.Token, ct);
        return result.Succeeded
            ? Ok(new ApiEnvelope<VerifyEmailResponse>(result.Value))
            : Failure(result);
    }

    [AllowAnonymous, HttpPost("forgot-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!await VerifyCaptchaAsync(request.CaptchaToken, CaptchaAction.ForgotPassword, ct))
        {
            return InvalidCaptcha();
        }

        var result = await authService.ForgotPasswordAsync(request.Email, ct);
        return result.Succeeded
            ? Accepted(new ApiEnvelope<ForgotPasswordResponse>(result.Value))
            : Failure(result);
    }

    [AllowAnonymous, HttpPost("reset-password/check"), EnableRateLimiting("auth")]
    public async Task<IActionResult> CheckPasswordResetToken(
        CheckPasswordResetTokenRequest request,
        CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await authService.CheckPasswordResetTokenAsync(request.Token, ct);
        return result.Succeeded
            ? Ok(new ApiEnvelope<PasswordResetTokenStatusResponse>(result.Value))
            : Failure(result);
    }

    [AllowAnonymous, HttpPost("reset-password"), EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var result = await authService.ResetPasswordAsync(request.Token, request.NewPassword, ct);
        return result.Succeeded
            ? Ok(new ApiEnvelope<ResetPasswordResponse>(result.Value))
            : Failure(result);
    }

    [Authorize, HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var sessions = await authService.GetActiveSessionsAsync(userId, CurrentSessionId(), ct);
        return Ok(new ApiEnvelope<IReadOnlyCollection<ActiveSessionResponse>>(sessions));
    }

    [Authorize, HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var revoked = await authService.RevokeSessionAsync(userId, sessionId, ct);
        return revoked
            ? NoContent()
            : NotFound(new ApiErrorEnvelope(new(ErrorCodes.NotFound, "That session was not found.")));
    }

    [Authorize, HttpPost("sessions/revoke-others")]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var revoked = await authService.RevokeOtherSessionsAsync(userId, CurrentSessionId(), ct);
        return Ok(new ApiEnvelope<RevokeOtherSessionsResponse>(new(revoked)));
    }

    [Authorize, HttpPost("sessions/revoke-all")]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken ct)
    {
        if (CurrentUserId() is not Guid userId)
        {
            return Unauthorized();
        }

        var revoked = await authService.RevokeAllSessionsAsync(userId, ct);
        return Ok(new ApiEnvelope<RevokeOtherSessionsResponse>(new(revoked)));
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        var user = await authService.GetCurrentUserAsync(userId, ct);
        return user is null ? NotFound(new ApiErrorEnvelope(new(ErrorCodes.NotFound, "User was not found."))) : Ok(new ApiEnvelope<CurrentUserResponse>(user));
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    /// <summary>
    /// The session behind the calling access token. The JWT handler maps the
    /// "sid" claim to ClaimTypes.Sid by default, so both spellings are read and
    /// the lookup keeps working if that mapping is ever turned off.
    /// </summary>
    private Guid? CurrentSessionId()
    {
        var value = User.FindFirstValue(ClaimTypes.Sid) ?? User.FindFirstValue("sid");
        return Guid.TryParse(value, out var sessionId) ? sessionId : null;
    }

    private ClientInfo CurrentClient()
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        return new ClientInfo(
            string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            HttpContext.Connection.RemoteIpAddress?.ToString());
    }

    private async Task<IActionResult?> ValidateAsync<T>(T request, CancellationToken ct)
    {
        var validator = services.GetRequiredService<IValidator<T>>();
        var result = await validator.ValidateAsync(request, ct);
        if (result.IsValid)
        {
            return null;
        }

        var details = result.Errors.GroupBy(x => x.PropertyName).ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray());
        logger.LogWarning(
            "Authentication request validation failed for fields: {ValidationFields}",
            string.Join(", ", details.Keys));
        return BadRequest(new ApiErrorEnvelope(new(ErrorCodes.ValidationError, "The request is invalid.", details)));
    }

    private IActionResult Failure<T>(AuthResult<T> result)
    {
        var (status, code, message) = result.Failure switch
        {
            AuthFailure.DuplicateEmail => (409, ErrorCodes.EmailAlreadyExists, "An account already uses this email. Try signing in instead."),
            AuthFailure.AccountLocked => (423, ErrorCodes.AccountLocked, "Too many failed sign-in attempts. Please wait 15 minutes, or reset your password."),
            AuthFailure.InvalidRefreshToken => (401, ErrorCodes.InvalidRefreshToken, "The refresh token is invalid or expired."),
            AuthFailure.InactiveAccount => (403, ErrorCodes.AccountInactive, "This account is inactive. Contact support if you think that is a mistake."),
            AuthFailure.VerificationCooldown => (429, ErrorCodes.VerificationEmailCooldown, "Please wait before requesting another verification email."),
            AuthFailure.InvalidVerificationToken => (400, ErrorCodes.InvalidVerificationToken, "The verification link is invalid or has already been used."),
            AuthFailure.ExpiredVerificationToken => (400, ErrorCodes.ExpiredVerificationToken, "The verification link has expired. Please request a new one."),
            AuthFailure.PasswordResetCooldown => (429, ErrorCodes.PasswordResetCooldown, "Please wait before requesting another password reset email."),
            AuthFailure.InvalidPasswordResetToken => (400, ErrorCodes.InvalidPasswordResetToken, "The reset link is invalid or has already been used."),
            AuthFailure.ExpiredPasswordResetToken => (400, ErrorCodes.ExpiredPasswordResetToken, "The reset link has expired. Please request a new one."),
            AuthFailure.PasswordReused => (400, ErrorCodes.PasswordReused, "Your new password must be different from your current password."),
            _ => (401, ErrorCodes.InvalidCredentials, "The email or password is incorrect. Please try again.")
        };
        if (result.RetryAfterSeconds is int seconds)
        {
            Response.Headers.RetryAfter = seconds.ToString();
        }

        return StatusCode(status, new ApiErrorEnvelope(new ApiError(code, message, result.RetryAfterSeconds is int retry ? new
        {
            retryAfterSeconds = retry
        } : null)));
    }

    private Task<bool> VerifyCaptchaAsync(string token, string action, CancellationToken ct) =>
        recaptchaVerifier.VerifyAsync(token, action, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

    private IActionResult InvalidCaptcha()
    {
        logger.LogWarning("Registration was rejected because reCAPTCHA verification failed.");
        return BadRequest(new ApiErrorEnvelope(
            new ApiError(ErrorCodes.CaptchaInvalid, "reCAPTCHA verification failed. Please try again.")));
    }
}
