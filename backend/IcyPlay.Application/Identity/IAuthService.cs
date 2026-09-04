namespace IcyPlay.Application.Identity;

public interface IAuthService
{
    Task<AuthResult<RegistrationResponse>> RegisterCustomerAsync(
        RegisterCustomerRequest request,
        CancellationToken cancellationToken);

    Task<AuthResult<RegistrationResponse>> RegisterFacilityOwnerAsync(
        RegisterFacilityOwnerRequest request,
        CancellationToken cancellationToken);

    Task<AuthResult<TokenResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken);

    Task<AuthResult<TokenResponse>> RefreshAsync(
        string refreshToken,
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

    Task<CurrentUserResponse?> GetCurrentUserAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
