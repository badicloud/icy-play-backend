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
    [AllowAnonymous, HttpPost("register/customer")]
    public async Task<IActionResult> RegisterCustomer(RegisterCustomerRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!await VerifyCaptchaAsync(request.CaptchaToken, ct))
        {
            return InvalidCaptcha();
        }

        var result = await authService.RegisterCustomerAsync(request, ct);
        return result.Succeeded ? StatusCode(StatusCodes.Status201Created, new ApiEnvelope<RegistrationResponse>(result.Value)) : Failure(result);
    }

    [AllowAnonymous, HttpPost("register/facility-owner")]
    public async Task<IActionResult> RegisterFacilityOwner(RegisterFacilityOwnerRequest request, CancellationToken ct)
    {
        var invalid = await ValidateAsync(request, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (!await VerifyCaptchaAsync(request.CaptchaToken, ct))
        {
            return InvalidCaptcha();
        }

        var result = await authService.RegisterFacilityOwnerAsync(request, ct);
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

        var result = await authService.LoginAsync(request, ct);
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

        var result = await authService.RefreshAsync(request.RefreshToken, ct);
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
            AuthFailure.DuplicateEmail => (409, ErrorCodes.EmailAlreadyExists, "An account with this email already exists."),
            AuthFailure.AccountLocked => (423, ErrorCodes.AccountLocked, "Too many failed login attempts. Please try again later."),
            AuthFailure.InvalidRefreshToken => (401, ErrorCodes.InvalidRefreshToken, "The refresh token is invalid or expired."),
            AuthFailure.InactiveAccount => (403, ErrorCodes.AccountInactive, "The account is inactive."),
            AuthFailure.VerificationCooldown => (429, ErrorCodes.VerificationEmailCooldown, "Please wait before requesting another verification email."),
            AuthFailure.InvalidVerificationToken => (400, ErrorCodes.InvalidVerificationToken, "The verification link is invalid or has already been used."),
            AuthFailure.ExpiredVerificationToken => (400, ErrorCodes.ExpiredVerificationToken, "The verification link has expired. Please request a new one."),
            _ => (401, ErrorCodes.InvalidCredentials, "Invalid email or password.")
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

    private Task<bool> VerifyCaptchaAsync(string token, CancellationToken ct) =>
        recaptchaVerifier.VerifyAsync(token, "register", HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

    private IActionResult InvalidCaptcha()
    {
        logger.LogWarning("Registration was rejected because reCAPTCHA verification failed.");
        return BadRequest(new ApiErrorEnvelope(
            new ApiError(ErrorCodes.CaptchaInvalid, "reCAPTCHA verification failed. Please try again.")));
    }
}
