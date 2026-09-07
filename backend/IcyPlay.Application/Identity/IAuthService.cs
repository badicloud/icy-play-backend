namespace IcyPlay.Application.Identity;

public interface IAuthService
{
    Task<AuthResult<RegistrationResponse>> RegisterCustomerAsync(
        RegisterCustomerRequest request,
        CancellationToken cancellationToken);

    Task<AuthResult<TokenResponse>> LoginAsync(
        LoginRequest request,
        ClientInfo client,
        CancellationToken cancellationToken);

    Task<AuthResult<TokenResponse>> RefreshAsync(
        string refreshToken,
        ClientInfo client,
        CancellationToken cancellationToken);

    Task<AuthResult<bool>> LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken);

    Task<AuthResult<ResendVerificationEmailResponse>> ResendVerificationEmailAsync(
        string email,
        CancellationToken cancellationToken);

    Task<AuthResult<VerifyEmailResponse>> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken);

    Task<AuthResult<ForgotPasswordResponse>> ForgotPasswordAsync(
        string email,
        CancellationToken cancellationToken);

    Task<AuthResult<PasswordResetTokenStatusResponse>> CheckPasswordResetTokenAsync(
        string token,
        CancellationToken cancellationToken);

    Task<AuthResult<ResetPasswordResponse>> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ActiveSessionResponse>> GetActiveSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken);

    Task<bool> RevokeSessionAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken);

    Task<int> RevokeOtherSessionsAsync(
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken);

    Task<int> RevokeAllSessionsAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<CurrentUserResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
