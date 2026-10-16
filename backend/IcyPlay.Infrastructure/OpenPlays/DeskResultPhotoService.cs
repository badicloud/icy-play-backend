using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// The desk's photos of a session's results. The files go from the browser to
/// Cloudinary; this checks the link is the platform's own, in the results
/// folder, and keeps one live photo of each kind.
/// </summary>
public sealed class DeskResultPhotoService(
    AppDbContext db,
    IAuditLogger audit,
    ICloudinaryAssetService assets,
    TimeProvider timeProvider) : IDeskResultPhotoService
{
    public async Task<OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>> SaveAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        string kind,
        OpenPlayPhotoInput photo,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(photo);

        var (door, sessionId, refusal) = await SessionAsync(actor, openPlayId, date, kind, ct);

        if (refusal is OpenPlayRegistrationFailure refused)
        {
            return Fail(refused);
        }

        // Only a link on the platform's own Cloudinary account, in the folder
        // the desk's signature allows: anything else could point the players'
        // download at a file of somebody else's choosing.
        if (string.IsNullOrWhiteSpace(photo.PublicId)
            || !photo.PublicId.StartsWith(OpenPlayPhotos.ResultsFolder + "/", StringComparison.Ordinal)
            || !assets.IsTrustedSecureUrl(photo.SecureUrl))
        {
            return Fail(OpenPlayRegistrationFailure.InvalidResultPhoto);
        }

        var now = timeProvider.GetUtcNow();

        foreach (var earlier in await LiveAsync(sessionId, kind, now, ct))
        {
            earlier.Retire(now);
        }

        db.OpenPlaySessionPhotos.Add(new OpenPlaySessionPhoto(
            sessionId, kind, photo.PublicId, photo.SecureUrl, actor.UserId!.Value, now));
        Record(actor, AuditAction.OpenPlayResultPhotoTaken, door!, kind, "Result photo taken.");
        await db.SaveChangesAsync(ct);

        return OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>.Success(await PhotosAsync(db, sessionId, now, ct));
    }

    public async Task<OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>> RemoveAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        string kind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var (door, sessionId, refusal) = await SessionAsync(actor, openPlayId, date, kind, ct);

        if (refusal is OpenPlayRegistrationFailure refused)
        {
            return Fail(refused);
        }

        var now = timeProvider.GetUtcNow();
        var live = await LiveAsync(sessionId, kind, now, ct);

        if (live.Count > 0)
        {
            foreach (var shown in live)
            {
                shown.Retire(now);
            }

            Record(actor, AuditAction.OpenPlayResultPhotoRemoved, door!, kind, "Result photo removed.");
            await db.SaveChangesAsync(ct);
        }

        return OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>.Success(await PhotosAsync(db, sessionId, now, ct));
    }

    /// <summary>The session's photos still shown, one of each kind at most: for the matches board too.</summary>
    internal static async Task<IReadOnlyList<MatchResultPhoto>> PhotosAsync(
        AppDbContext db,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken ct) =>
        await db.OpenPlaySessionPhotos
            .AsNoTracking()
            .Where(photo => photo.SessionId == sessionId && photo.RetiredAt == null && photo.ExpiresAt > now)
            .OrderBy(photo => photo.CreatedAt)
            .Select(photo => new MatchResultPhoto(photo.Kind, photo.SecureUrl, photo.CreatedAt, photo.ExpiresAt))
            .ToListAsync(ct);

    private Task<List<OpenPlaySessionPhoto>> LiveAsync(Guid sessionId, string kind, DateTimeOffset now, CancellationToken ct) =>
        db.OpenPlaySessionPhotos
            .Where(photo => photo.SessionId == sessionId && photo.Kind == kind && photo.RetiredAt == null && photo.ExpiresAt > now)
            .ToListAsync(ct);

    /// <summary>The session, at a venue this person works, once it is over.</summary>
    private async Task<(SessionDoor? Door, Guid SessionId, OpenPlayRegistrationFailure? Refusal)> SessionAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        string kind,
        CancellationToken ct)
    {
        var door = await SessionDoor.FindAsync(db, timeProvider, actor.UserId ?? Guid.Empty, openPlayId, date, ct);

        if (door is null)
        {
            return (null, Guid.Empty, OpenPlayRegistrationFailure.NotFound);
        }

        if (!OpenPlayResultPhotoKind.All.Contains(kind))
        {
            return (door, Guid.Empty, OpenPlayRegistrationFailure.InvalidResultPhoto);
        }

        if (!door.IsOver)
        {
            return (door, Guid.Empty, OpenPlayRegistrationFailure.SessionNotOver);
        }

        var sessionId = await db.OpenPlaySessions
            .Where(session => session.OpenPlayId == openPlayId && session.Date == date)
            .Select(session => (Guid?)session.Id)
            .SingleOrDefaultAsync(ct);

        return sessionId is Guid id
            ? (door, id, null)
            : (door, Guid.Empty, OpenPlayRegistrationFailure.NotFound);
    }

    private static OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>> Fail(OpenPlayRegistrationFailure failure) =>
        OpenPlayRegistrationResult<IReadOnlyList<MatchResultPhoto>>.Fail(failure);

    /// <summary>Against the open play, so it shows in the owner's activity.</summary>
    private void Record(AuditActor actor, string action, SessionDoor door, string kind, string reason) =>
        audit.RecordEvent(
            actor,
            action,
            AuditEntityType.OpenPlay,
            door.OpenPlay.Id,
            new Dictionary<string, string?>
            {
                ["date"] = door.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["photo"] = kind
            },
            reason);
}
