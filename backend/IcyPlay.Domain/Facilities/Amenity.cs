using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// A lookup rather than an enum, so the platform team can add one without a
/// deploy and the customer-facing filter has something to read.
/// </summary>
public sealed class Amenity : Entity
{
    private Amenity()
    {
    }

    public Amenity(string key, string name, string category, int displayOrder, DateTimeOffset createdAt)
    {
        Key = key.Trim().ToLowerInvariant();
        Name = name.Trim();
        Category = category.Trim();
        DisplayOrder = displayOrder;
        CreatedAt = createdAt;
    }

    /// <summary>Stable identifier for code and for the customer-facing filter.</summary>
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public int DisplayOrder
    {
        get; private set;
    }
    public bool IsActive { get; private set; } = true;
}

public static class AmenityCategory
{
    public const string Safety = "Safety";
    public const string Comfort = "Comfort";
    public const string Access = "Access";
    public const string Equipment = "Equipment";
}
