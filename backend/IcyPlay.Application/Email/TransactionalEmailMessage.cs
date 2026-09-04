namespace IcyPlay.Application.Email;

public sealed record TransactionalEmailMessage(
    string TemplateKey,
    string RecipientEmail,
    string RecipientName,
    IReadOnlyDictionary<string, object> Variables);
