namespace IcyPlay.Application.Email;

public interface IPasswordResetEmailService
{
    Task SendAsync(
        Guid userId,
        string recipientEmail,
        string recipientName,
        CancellationToken cancellationToken);
}
