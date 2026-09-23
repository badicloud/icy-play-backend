namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// The moment, on a venue's wall clock.
///
/// Opening hours, peak windows, holidays and "has this hour gone" are all
/// written in the venue's own zone, so they have to be asked there too. Handing
/// this UTC in Manila answers for eight hours ago.
///
/// One copy because there were three: the booking engine's, the desk's, and
/// nearly a fourth for the reports. Three answers to one question is three
/// chances for a venue to run a third of a day wrong in one of them.
/// </summary>
internal static class VenueClock
{
    /// <summary>
    /// An unrecognised zone falls back to UTC rather than throwing: a court
    /// that cannot be looked at is worse than one whose cut-off is off by the
    /// offset. That fallback is a last resort and not a safety net — it is
    /// silent, and in Manila it is eight hours wrong. What keeps a venue out of
    /// it is <c>TimeZoneRules</c>, which refuses a zone the platform cannot
    /// read at the moment somebody types it.
    /// </summary>
    public static DateTimeOffset LocalNowIn(string? timeZone, DateTimeOffset utcNow) =>
        timeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(utcNow, zone)
            : utcNow;
}
