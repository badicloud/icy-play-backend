using IcyPlay.Application.Audit;
using IcyPlay.Application.Common;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The desk's queue of open play registrations: receipts waiting to be
/// checked, and the ones already confirmed. Owners and attendants, on the
/// venues they work.
/// </summary>
public interface IDeskOpenPlayRequestService
{
    Task<OpenPlayRegistrationResult<PagedResult<DeskOpenPlayRequest>>> ListAsync(
        Guid userId,
        string tab,
        Guid? facilityId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>The payment is good: the player is registered, and told.</summary>
    Task<OpenPlayRegistrationResult<DeskOpenPlayRequest>> ConfirmAsync(
        AuditActor actor,
        Guid registrationId,
        CancellationToken cancellationToken);

    /// <summary>The payment is not good: the spot is released, and the player told why.</summary>
    Task<OpenPlayRegistrationResult<DeskOpenPlayRequest>> RejectAsync(
        AuditActor actor,
        Guid registrationId,
        RejectOpenPlayRequest request,
        CancellationToken cancellationToken);
}
