namespace IcyPlay.Application.Email;

public interface ITransactionalEmailSender
{
    Task SendAsync(
        TransactionalEmailMessage message,
        CancellationToken cancellationToken);
}
