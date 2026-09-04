namespace IcyPlay.Application.Email;

public interface IEmailTemplateStore
{
    Task<EmailTemplateDescriptor?> GetActiveAsync(
        string key,
        string provider,
        CancellationToken cancellationToken);
}
