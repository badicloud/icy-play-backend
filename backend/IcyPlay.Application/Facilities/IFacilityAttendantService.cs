using IcyPlay.Application.Audit;

namespace IcyPlay.Application.Facilities;

public interface IFacilityAttendantService
{
    /// <summary>
    /// Who works this venue's desk, the owner first. Scoped to the owner as
    /// well as the facility, so another owner's venue answers the same as one
    /// that does not exist.
    /// </summary>
    /// <summary>
    /// The facility owner an account is, or null when it is nobody's. How the
    /// owner's own desk turns who is signed in into the owner the calls below
    /// act for — never an id the request sends.
    /// </summary>
    Task<Guid?> FacilityOwnerIdOfAsync(Guid userId, CancellationToken ct);

    Task<AttendantResult<IReadOnlyCollection<FacilityAttendantDetail>>> ListAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        CancellationToken ct);

    /// <summary>
    /// What the console may do with an address, asked while it is being typed.
    /// An address nobody holds answers <see cref="AttendantEmailStatus.Available"/>.
    /// </summary>
    Task<AttendantResult<AttendantEmailCheck>> CheckEmailAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        string email,
        CancellationToken ct);

    /// <summary>
    /// Puts somebody on the desk and emails them an invitation.
    ///
    /// The address must be one nobody holds. One address, one account: an
    /// address that already belongs to somebody is refused rather than handed
    /// a desk, so an admin who mistypes a colleague's address is told instead
    /// of silently giving a stranger the run of a venue's bookings.
    ///
    /// That includes somebody taken off this desk before. Their row stays
    /// retired and their address stays spoken for: putting them back is a
    /// different gesture from adding somebody, and this is not it.
    /// </summary>
    Task<AttendantResult<FacilityAttendantDetail>> InviteAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        InviteAttendantRequest request,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// Sends an attendant who has not claimed their account another activation
    /// link. Any link still outstanding stops working, so a resend cannot leave
    /// two live invitations behind it.
    /// </summary>
    Task<AttendantResult<bool>> ResendInvitationAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        Guid attendantId,
        AuditActor actor,
        CancellationToken ct);

    /// <summary>
    /// Takes somebody off the desk. The row stays: a booking they confirmed
    /// still has to have somebody's name against it.
    /// </summary>
    Task<AttendantResult<bool>> RemoveAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        Guid attendantId,
        string? reason,
        AuditActor actor,
        CancellationToken ct);
}
