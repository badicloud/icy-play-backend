using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Payments;

/// <summary>
/// An event the gateway has sent us, kept by its id.
///
/// Gateways send again when they are not sure an event arrived, and the same
/// payment must not confirm a booking twice or send two letters. The id is
/// unique in the database, so a second delivery is turned away by the insert
/// rather than by a check somebody could race.
/// </summary>
public sealed class PaymentWebhookEvent : Entity
{
    private PaymentWebhookEvent()
    {
    }

    public PaymentWebhookEvent(string provider, string eventId, string eventType, DateTimeOffset receivedAt)
    {
        Provider = provider;
        EventId = eventId;
        EventType = eventType;
        CreatedAt = receivedAt;
    }

    public string Provider { get; private set; } = string.Empty;

    public string EventId { get; private set; } = string.Empty;

    public string EventType { get; private set; } = string.Empty;
}
