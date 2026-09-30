using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The desk's open plays: create and change a draft, publish it, take it back
/// to a draft, delete a draft, and end a published one.
///
/// Owners and attendants both, on the venues they work. A venue somebody does
/// not work answers the same as one that does not exist.
/// </summary>
public interface IDeskOpenPlayService
{
    Task<OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>> ListAsync(
        Guid userId,
        Guid? facilityId,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> GetAsync(Guid userId, Guid openPlayId, CancellationToken cancellationToken);

    /// <summary>Saves a new draft. Clashes come back as warnings, because a draft holds nothing.</summary>
    Task<OpenPlayResult<OpenPlaySaved>> CreateAsync(
        AuditActor actor,
        OpenPlayInput input,
        CancellationToken cancellationToken);

    /// <summary>Changes a draft. A published open play cannot be changed.</summary>
    Task<OpenPlayResult<OpenPlaySaved>> UpdateAsync(
        AuditActor actor,
        Guid openPlayId,
        OpenPlayInput input,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens it for registration and holds the court. Refused while anything
    /// clashes: a booking, or another published open play.
    /// </summary>
    Task<OpenPlayResult<DeskOpenPlay>> PublishAsync(AuditActor actor, Guid openPlayId, CancellationToken cancellationToken);

    /// <summary>Back to a draft, releasing the court. Refused once anybody has registered.</summary>
    Task<OpenPlayResult<DeskOpenPlay>> UnpublishAsync(AuditActor actor, Guid openPlayId, CancellationToken cancellationToken);

    /// <summary>Ends a published one after today, releasing every later date.</summary>
    Task<OpenPlayResult<DeskOpenPlay>> EndAsync(AuditActor actor, Guid openPlayId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets or replaces the cover photo. Allowed after publishing too: the photo
    /// is the one thing outside the lock. The URL must be on the platform's own
    /// Cloudinary account, in the open play folder.
    /// </summary>
    Task<OpenPlayResult<DeskOpenPlay>> SetCoverPhotoAsync(
        AuditActor actor,
        Guid openPlayId,
        OpenPlayPhotoInput photo,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> RemoveCoverPhotoAsync(
        AuditActor actor,
        Guid openPlayId,
        CancellationToken cancellationToken);

    /// <summary>Deletes a draft. Only a draft: nobody can have registered for it.</summary>
    Task<OpenPlayResult<bool>> DeleteDraftAsync(AuditActor actor, Guid openPlayId, CancellationToken cancellationToken);
}
