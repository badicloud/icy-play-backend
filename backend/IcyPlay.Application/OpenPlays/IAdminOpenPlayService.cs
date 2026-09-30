using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The platform admin's view of one facility owner's open plays, with every
/// action the desk has and under the same rules: a clash still refuses a
/// publish, and a published open play is still locked except for its photo.
///
/// The owner is the scope. An open play or court at another owner's venue
/// answers as not found, so a link built for the wrong owner does nothing.
/// </summary>
public interface IAdminOpenPlayService
{
    Task<OpenPlayResult<IReadOnlyCollection<DeskOpenPlay>>> ListForOwnerAsync(
        Guid facilityOwnerId,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> GetForOwnerAsync(
        Guid facilityOwnerId,
        Guid openPlayId,
        CancellationToken cancellationToken);

    /// <summary>The owner's venues, each with its own today, and their courts: what the form picks from.</summary>
    Task<OpenPlayPlaces> PlacesForOwnerAsync(Guid facilityOwnerId, CancellationToken cancellationToken);

    Task<OpenPlayResult<OpenPlaySaved>> CreateForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        OpenPlayInput input,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<OpenPlaySaved>> UpdateForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        OpenPlayInput input,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> PublishForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> UnpublishForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> EndForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> SetCoverPhotoForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        OpenPlayPhotoInput photo,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<DeskOpenPlay>> RemoveCoverPhotoForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        CancellationToken cancellationToken);

    Task<OpenPlayResult<bool>> DeleteDraftForOwnerAsync(
        Guid facilityOwnerId,
        AuditActor actor,
        Guid openPlayId,
        CancellationToken cancellationToken);
}
