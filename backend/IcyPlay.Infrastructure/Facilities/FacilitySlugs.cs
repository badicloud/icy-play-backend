using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// Finding a free slug is shared between onboarding and adding a facility
/// later. Two copies would eventually disagree about how a collision is
/// resolved, and the slug is the public web address.
/// </summary>
public static class FacilitySlugs
{
    /// <summary>
    /// Suffixed rather than rejected, because two venues legitimately share a
    /// name across two cities.
    /// </summary>
    public static async Task<string> ReserveAsync(AppDbContext db, string name, CancellationToken ct)
    {
        var baseSlug = Facility.ToSlug(name);
        var taken = await db.Facilities
            .Where(facility => facility.Slug == baseSlug || facility.Slug.StartsWith(baseSlug + "-"))
            .Select(facility => facility.Slug)
            .ToListAsync(ct);

        if (!taken.Contains(baseSlug))
        {
            return baseSlug;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseSlug}-{suffix}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
