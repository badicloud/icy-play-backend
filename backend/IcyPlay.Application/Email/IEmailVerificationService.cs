namespace IcyPlay.Application.Email;

public interface IEmailVerificationService
{
    Task SendAsync(
        Guid userId,
        string recipientEmail,
        string recipientName,
        CancellationToken cancellationToken);
}
