using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Email;

public sealed class EmailTemplate : Entity
{
    private EmailTemplate()
    {
    }

    public EmailTemplate(
        string key,
        string provider,
        long externalTemplateId,
        string subject)
    {
        Key = key.Trim();
        Provider = provider.Trim();
        ExternalTemplateId = externalTemplateId;
        Subject = subject.Trim();
    }

    public string Key { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public long ExternalTemplateId
    {
        get; private set;
    }
    public string Subject { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
}
