namespace IcyPlay.Infrastructure.Email;

public sealed class MailjetOptions
{
    public const string SectionName = "Mailjet";

    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string SenderEmail { get; init; } = string.Empty;
    public string SenderName { get; init; } = "IcyPlay";
}
