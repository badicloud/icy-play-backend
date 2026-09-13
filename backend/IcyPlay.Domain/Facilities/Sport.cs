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

    public Sport(string key, string name, string category, int displayOrder, DateTimeOffset createdAt)
    {
        Key = key.Trim().ToLowerInvariant();
        Name = name.Trim();
        Category = category.Trim();
        DisplayOrder = displayOrder;
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

    public void Rename(string name, string category, int displayOrder, DateTimeOffset now)
    {
        Name = name.Trim();
        Category = category.Trim();
        DisplayOrder = displayOrder;
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
    public const string Other = "Other";

    public static readonly IReadOnlyCollection<string> All = [Court, Racket, Combat, Other];

    public static bool IsSupported(string value) => All.Contains(value);
}
