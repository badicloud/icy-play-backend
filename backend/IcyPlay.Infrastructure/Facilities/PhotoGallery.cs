using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// The one place that writes a gallery, for a facility or for a court. Three
/// callers now build one — onboarding, the court wizard and editing — and the
/// cover rule has to read the same in all of them, or a venue ends up with two
/// covers depending on which screen last touched it.
/// </summary>
internal static class PhotoGallery
{
    /// <summary>
    /// Writes a gallery for a subject that has none yet. When nothing was
    /// flagged as the cover the first picture becomes it: a gallery with no
    /// cover has nothing to show in the booking list, and the first is the best
    /// guess available.
    /// </summary>
    public static void Add(
        AppDbContext db,
        Guid facilityId,
        Guid? courtId,
        IReadOnlyCollection<PhotoInput> photos,
        DateTimeOffset now)
    {
        if (photos.Count == 0)
        {
            return;
        }

        var ordered = photos.OrderBy(photo => photo.DisplayOrder).ToArray();
        var coverPublicId = CoverOf(ordered);

        foreach (var photo in ordered)
        {
            // Through the set, because a client-generated key added only to a
            // tracked navigation is read by EF as a row that already exists.
            db.Photos.Add(new Photo(
                facilityId,
                courtId,
                courtId is null ? null : photo.SportId,
                photo.PublicId,
                photo.SecureUrl,
                photo.Caption,
                photo.DisplayOrder,
                photo.PublicId == coverPublicId,
                now));
        }
    }

    /// <summary>
    /// Answers the gallery as a whole, the way the amenity selection is
    /// answered: what the caller sends is what the subject keeps. Rows dropped
    /// here leave their file in Cloudinary, which costs little and is easier to
    /// live with than a delete that cannot be undone.
    /// </summary>
    public static async Task ReplaceAsync(
        AppDbContext db,
        Guid facilityId,
        Guid? courtId,
        IReadOnlyCollection<PhotoInput> photos,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await db.Photos
            .Where(photo => photo.FacilityId == facilityId && photo.CourtId == courtId)
            .ToListAsync(ct);

        var keptPublicIds = photos.Select(photo => photo.PublicId).ToHashSet();
        db.Photos.RemoveRange(existing.Where(photo => !keptPublicIds.Contains(photo.PublicId)));

        if (photos.Count == 0)
        {
            return;
        }

        var ordered = photos.OrderBy(photo => photo.DisplayOrder).ToArray();
        var coverPublicId = CoverOf(ordered);

        foreach (var photo in ordered)
        {
            var match = existing.FirstOrDefault(candidate => candidate.PublicId == photo.PublicId);

            if (match is null)
            {
                db.Photos.Add(new Photo(
                    facilityId,
                    courtId,
                    courtId is null ? null : photo.SportId,
                    photo.PublicId,
                    photo.SecureUrl,
                    photo.Caption,
                    photo.DisplayOrder,
                    photo.PublicId == coverPublicId,
                    now));
            }
            else
            {
                match.Describe(photo.Caption, photo.DisplayOrder, now);
                match.SetCover(photo.PublicId == coverPublicId, now);
                match.ShowsSport(courtId is null ? null : photo.SportId, now);
            }
        }
    }

    /// <summary>Reads back what a gallery amounts to, for the audit trail.</summary>
    public static Dictionary<string, string?> Snapshot(IReadOnlyCollection<PhotoInput> photos)
    {
        var ordered = photos.OrderBy(photo => photo.DisplayOrder).ToArray();

        return new Dictionary<string, string?>
        {
            ["photos"] = photos.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["cover"] = ordered.Length == 0 ? null : CoverOf(ordered)
        };
    }

    private static string CoverOf(IReadOnlyList<PhotoInput> ordered) =>
        ordered.FirstOrDefault(photo => photo.IsCover)?.PublicId ?? ordered[0].PublicId;
}
