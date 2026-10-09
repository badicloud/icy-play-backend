namespace IcyPlay.Application.Email;

public sealed record TransactionalEmailMessage(
    string TemplateKey,
    string RecipientEmail,
    string RecipientName,
    IReadOnlyDictionary<string, object> Variables,
    /// <summary>Files sent with the letter, such as a receipt. None by default.</summary>
    IReadOnlyCollection<EmailAttachment>? Attachments = null);

/// <summary>A file sent with a letter. Small: it travels inside the request to the mail provider.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
