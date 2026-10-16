using IcyPlay.Application.Storage;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Deletes the session result photos that are due: two days after they were
/// taken, or as soon as the desk replaced or removed them. Run every hour by
/// Hangfire. A photo Cloudinary would not delete stays, to try again next time.
/// </summary>
public sealed class OpenPlayResultPhotoCleanup(
    AppDbContext db,
    ICloudinaryAssetRemover remover,
    TimeProvider timeProvider,
    ILogger<OpenPlayResultPhotoCleanup> logger)
{
    /// <summary>At most this many a run, so one slow run cannot hold the job up.</summary>
    private const int Batch = 200;

    public const string JobId = "open-play-result-photo-cleanup";

    /// <returns>How many photos were deleted.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await db.OpenPlaySessionPhotos
            .Where(photo => photo.ExpiresAt <= now)
            .OrderBy(photo => photo.ExpiresAt)
            .Take(Batch)
            .ToListAsync(cancellationToken);

        var deleted = 0;

        foreach (var photo in due)
        {
            if (await remover.DestroyAsync(photo.PublicId, cancellationToken))
            {
                db.OpenPlaySessionPhotos.Remove(photo);
                deleted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation("Deleted {Count} open play result photos past their two days.", deleted);
        }

        return deleted;
    }
}
