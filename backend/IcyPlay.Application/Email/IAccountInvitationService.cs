namespace IcyPlay.Application.Email;

/// <summary>
/// The invitation an admin-created account is claimed with. Separate from the
/// password reset flow because the meaning is different: this hands an account
/// to the person it was made for, rather than returning one to someone who
/// already had it.
/// </summary>
public interface IAccountInvitationService
{
    /// <summary>
    /// Issues a fresh invitation and emails it. Any invitation already
    /// outstanding for the user is retired first, so only the newest link works.
    ///
    /// The token, the expiry and the activation page are the same whoever is
    /// being invited; only the letter differs, which is why the template is a
    /// parameter rather than a second copy of this method.
    /// </summary>
    Task SendAsync(InvitationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// What the activation page shows before asking for a password. The token
    /// is the secret, so holding it is what entitles the caller to these
    /// details.
    /// </summary>
    Task<InvitationDetails?> CheckAsync(string rawToken, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the first password and consumes the invitation. Clicking a link
    /// sent to an address proves control of it, so this verifies the email at
    /// the same time rather than asking for a second round trip.
    /// </summary>
    Task<InvitationAcceptance> AcceptAsync(
        string rawToken,
        string password,
        CancellationToken cancellationToken);
}

/// <summary>
/// Who is being invited, and which letter to send them.
/// </summary>
/// <param name="TemplateKey">
/// One of the invitation templates. Defaults to the facility owner's, which is
/// the older of the two and what every existing caller means.
/// </param>
/// <param name="FacilityName">
/// The venue an attendant is being put on. Null for an owner, who is not being
/// invited to any one venue in particular.
/// </param>
public sealed record InvitationRequest(
    Guid UserId,
    string RecipientEmail,
    string RecipientName,
    string BusinessName,
    string TemplateKey = IcyPlay.Domain.Email.EmailTemplateKey.FacilityOwnerInvitation,
    string? FacilityName = null);

public sealed record InvitationDetails(
    string FullName,
    string Email,
    string? PhoneNumber,
    string? BusinessName,
    DateTimeOffset ExpiresAt);

public enum InvitationAcceptance
{
    Accepted,
    InvalidToken,
    ExpiredToken,
    AlreadyAccepted
}
