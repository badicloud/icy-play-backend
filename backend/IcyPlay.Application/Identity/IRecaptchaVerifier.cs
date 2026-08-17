namespace IcyPlay.Application.Identity;

public interface IRecaptchaVerifier
{
    Task<bool> VerifyAsync(
        string token,
        string expectedAction,
        string? remoteIp,
        CancellationToken cancellationToken);
}
