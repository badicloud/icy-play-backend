namespace IcyPlay.Infrastructure.Email;

/// <summary>
/// What the booking letters need that the booking itself cannot tell them: where
/// the site lives, and who to write to for help.
///
/// Configured rather than built from the request, the way the other email
/// services are. A link in an email outlives the request that sent it, and one
/// assembled from whatever host happened to answer is how a customer ends up
/// clicking through to localhost.
/// </summary>
public sealed class BookingNotificationOptions
{
    public const string SectionName = "BookingNotifications";

    /// <summary>The bookings page, without an id. One is appended.</summary>
    public string BookingUrl { get; init; } = string.Empty;

    public string SupportEmail { get; init; } = string.Empty;
}
