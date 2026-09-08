using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IcyPlay.Domain.Common;
using IcyPlay.Domain.Identity;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// A bookable venue belonging to a facility owner. One owner can hold several,
/// which is why this is its own record rather than columns on the owner.
/// </summary>
public sealed partial class Facility : Entity
{
    private Facility()
    {
    }

    public Facility(
        Guid facilityOwnerId,
        string name,
        string slug,
        string? description,
        FacilityAddress address,
        FacilityContact contact,
        FacilityPolicies policies,
        string timeZone,
        DateTimeOffset createdAt)
    {
        FacilityOwnerId = facilityOwnerId;
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = Clean(description);
        ApplyAddress(address);
        ApplyContact(contact);
        ApplyPolicies(policies);
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();
        CreatedAt = createdAt;
    }

    public const string DefaultTimeZone = "Asia/Manila";

    public Guid FacilityOwnerId
    {
        get; private set;
    }
    public FacilityOwner FacilityOwner { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The public URL segment. Stored rather than derived from the name so a
    /// rename cannot silently break every link already shared.
    /// </summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description
    {
        get; private set;
    }

    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2
    {
        get; private set;
    }
    public string City { get; private set; } = string.Empty;
    public string Province { get; private set; } = string.Empty;
    public string? PostalCode
    {
        get; private set;
    }
    public string Country { get; private set; } = string.Empty;

    /// <summary>
    /// Null until someone drops the pin. The address alone cannot give a
    /// customer directions, so a facility without coordinates is encoded but
    /// not yet mappable.
    /// </summary>
    public decimal? Latitude
    {
        get; private set;
    }
    public decimal? Longitude
    {
        get; private set;
    }

    /// <summary>IANA zone. Opening hours are wall-clock values read in it.</summary>
    public string TimeZone { get; private set; } = DefaultTimeZone;

    /// <summary>Public contact, deliberately separate from the billing contact.</summary>
    public string? ContactPhone
    {
        get; private set;
    }
    public string? ContactEmail
    {
        get; private set;
    }

    public string? SafetyMeasures
    {
        get; private set;
    }
    public string? HouseRules
    {
        get; private set;
    }

    public bool IsActive { get; private set; } = true;

    public ICollection<FacilityOperatingHour> OperatingHours { get; private set; } = [];
    public ICollection<FacilityAmenity> Amenities { get; private set; } = [];

    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    public void UpdateDetails(
        string name,
        string? description,
        FacilityAddress address,
        FacilityContact contact,
        FacilityPolicies policies,
        string timeZone,
        DateTimeOffset now)
    {
        Name = name.Trim();
        Description = Clean(description);
        ApplyAddress(address);
        ApplyContact(contact);
        ApplyPolicies(policies);
        TimeZone = string.IsNullOrWhiteSpace(timeZone) ? DefaultTimeZone : timeZone.Trim();
        UpdatedAt = now;
    }

    /// <summary>
    /// Set together, because a latitude without a longitude points nowhere.
    /// Passing null for either clears both.
    /// </summary>
    public void SetCoordinates(decimal? latitude, decimal? longitude, DateTimeOffset now)
    {
        if (latitude is null || longitude is null)
        {
            Latitude = null;
            Longitude = null;
        }
        else
        {
            if (latitude is < -90 or > 90)
            {
                throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
            }

            if (longitude is < -180 or > 180)
            {
                throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
            }

            Latitude = latitude;
            Longitude = longitude;
        }

        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    /// <summary>
    /// Turns a facility name into a URL segment. Uniqueness is the caller's
    /// problem: only the database can answer whether a slug is already taken.
    /// </summary>
    public static string ToSlug(string name)
    {
        var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            // Drop the accents FormD just split off, so "Parañaque" becomes
            // "paranaque" rather than losing the letter altogether.
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        var slug = NonSlugCharacters().Replace(builder.ToString().Normalize(NormalizationForm.FormC), "-");
        return slug.Trim('-');
    }

    private void ApplyAddress(FacilityAddress address)
    {
        AddressLine1 = address.Line1.Trim();
        AddressLine2 = Clean(address.Line2);
        City = address.City.Trim();
        Province = address.Province.Trim();
        PostalCode = Clean(address.PostalCode);
        Country = address.Country.Trim();
    }

    private void ApplyContact(FacilityContact contact)
    {
        ContactPhone = Clean(contact.Phone);
        ContactEmail = Clean(contact.Email)?.ToLowerInvariant();
    }

    private void ApplyPolicies(FacilityPolicies policies)
    {
        SafetyMeasures = Clean(policies.SafetyMeasures);
        HouseRules = Clean(policies.HouseRules);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharacters();
}

/// <summary>Grouped so the constructor does not take nine loose strings.</summary>
public sealed record FacilityAddress(
    string Line1,
    string? Line2,
    string City,
    string Province,
    string? PostalCode,
    string Country);

public sealed record FacilityContact(string? Phone, string? Email);

/// <summary>
/// Free text alongside the amenity checklist. The checklist is what a customer
/// filters on; this carries what no checklist can.
/// </summary>
public sealed record FacilityPolicies(string? SafetyMeasures, string? HouseRules);
