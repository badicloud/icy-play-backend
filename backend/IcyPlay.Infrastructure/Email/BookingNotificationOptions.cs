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

    /// <summary>
    /// The desk's upgrade queue. A whole URL rather than one built from a
    /// booking id: the attendant is being sent to a list of work, not to one
    /// customer's booking, and the list is what they will work down.
    /// </summary>
    public string UpgradesUrl { get; init; } = string.Empty;

    /// <summary>
    /// The desk's court bookings page. Where somebody goes to see a diary that
    /// has changed without them.
    /// </summary>
    public string CourtBookingsUrl { get; init; } = string.Empty;

    /// <summary>
    /// The desk's queue of payments waiting to be checked.
    ///
    /// The letter about a receipt used to offer only the receipt itself, which
    /// is the thing to look at but not the place to act: confirming happens in
    /// the queue, and an attendant who opened a picture had to go and find it.
    /// </summary>
    public string ConfirmationsUrl { get; init; } = string.Empty;

    public string SupportEmail { get; init; } = string.Empty;
}
