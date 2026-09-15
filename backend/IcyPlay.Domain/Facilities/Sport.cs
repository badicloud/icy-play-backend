using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// A sport a court can accommodate. Seeded with the common ones but managed
/// from the admin console afterwards, which is why this is a table rather than
/// an enum: a venue that hosts something nobody thought of should not need a
/// deploy.
/// </summary>
public sealed class Sport : Entity
{
    private Sport()
    {
    }

    public Sport(
        string key,
        string name,
        string category,
        int displayOrder,
        DateTimeOffset createdAt,
        string? kind = null)
    {
        Key = key.Trim().ToLowerInvariant();
        Name = name.Trim();
        Category = category.Trim();
        DisplayOrder = displayOrder;
        Kind = (kind ?? ActivityKind.Sport).Trim();
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Stable once created, like a slug. The customer-facing filter and any
    /// link that names a sport are built from it, so renaming the display name
    /// must not move it.
    /// </summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public int DisplayOrder
    {
        get; private set;
    }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Whether this is something played or something held. A court hosting a
    /// birthday party is not accommodating a sport, and a customer browsing for
    /// a game should not be offered one.
    /// </summary>
    public string Kind { get; private set; } = ActivityKind.Sport;

    public bool IsEvent => Kind == ActivityKind.Event;

    /// <summary>
    /// A stock picture of the sport, kept by the platform rather than by any
    /// venue. It is the last thing the booking list falls back to: a venue's
    /// own photo of the sport first, then the court's cover, then this.
    ///
    /// A card with no picture at all reads as a broken listing, and a new venue
    /// has none on the day it opens. The public id is the source of truth; the
    /// URL is stored beside it so a listing does not have to build one.
    /// </summary>
    public string? ImagePublicId
    {
        get; private set;
    }
    public string? ImageSecureUrl
    {
        get; private set;
    }

    public void Rename(
        string name,
        string category,
        int displayOrder,
        DateTimeOffset now,
        string? kind = null)
    {
        Name = name.Trim();
        Category = category.Trim();
        DisplayOrder = displayOrder;
        Kind = (kind ?? Kind).Trim();
        UpdatedAt = now;
    }

    /// <summary>
    /// Sets or clears the stock picture. Both parts move together: a URL
    /// without its public id cannot be re-derived at another size, and a public
    /// id without a URL shows nothing.
    /// </summary>
    public void Illustrate(string? publicId, string? secureUrl, DateTimeOffset now)
    {
        var id = string.IsNullOrWhiteSpace(publicId) ? null : publicId.Trim();
        var url = string.IsNullOrWhiteSpace(secureUrl) ? null : secureUrl.Trim();
        var complete = id is not null && url is not null;

        ImagePublicId = complete ? id : null;
        ImageSecureUrl = complete ? url : null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Retired, not deleted. Courts reference this row, and removing it would
    /// take their record of what they host with it.
    /// </summary>
    public void Retire(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}

/// <summary>
/// Fixed for now. Free text here would give "Racket" and "racket sports" as two
/// groups that mean the same thing.
/// </summary>
public static class SportCategory
{
    public const string Court = "Court sports";
    public const string Racket = "Racket sports";
    public const string Combat = "Combat";
    /// <summary>For the things a court is hired for rather than played at.</summary>
    public const string Events = "Events";
    public const string Other = "Other";

    public static readonly IReadOnlyCollection<string> All = [Court, Racket, Combat, Events, Other];

    public static bool IsSupported(string value) => All.Contains(value);
}

/// <summary>
/// A court is booked for two different kinds of thing: a game, and an occasion
/// that simply needs the floor. They are priced and divided the same way, so
/// they share a table — but a customer looking for a game must not be offered a
/// wedding, which is why the difference is recorded rather than inferred from
/// the category.
/// </summary>
public static class ActivityKind
{
    public const string Sport = "Sport";
    public const string Event = "Event";

    public static readonly IReadOnlyCollection<string> All = [Sport, Event];

    public static bool IsSupported(string value) => All.Contains(value);
}
