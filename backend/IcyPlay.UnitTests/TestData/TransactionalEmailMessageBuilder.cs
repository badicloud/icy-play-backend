using IcyPlay.Application.Email;
using IcyPlay.Domain.Email;

namespace IcyPlay.UnitTests.TestData;

public sealed class TransactionalEmailMessageBuilder
{
    private string templateKey = EmailTemplateKey.AccountVerification;
    private string recipientEmail = "customer@example.com";
    private string recipientName = "IcyPlay Customer";
    private IReadOnlyDictionary<string, object> variables =
        new Dictionary<string, object>
        {
            ["recipient_name"] = "IcyPlay Customer",
            ["verification_url"] = "https://example.com/verify-email?token=test-token",
            ["expiration_hours"] = 24,
            ["support_email"] = "support@example.com",
            ["current_year"] = 2026
        };

    public TransactionalEmailMessageBuilder WithTemplateKey(string value)
    {
        templateKey = value;
        return this;
    }

    public TransactionalEmailMessage Build()
    {
        return new TransactionalEmailMessage(
            templateKey,
            recipientEmail,
            recipientName,
            variables);
    }
}
