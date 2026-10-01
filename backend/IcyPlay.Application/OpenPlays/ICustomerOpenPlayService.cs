namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// A player joining an open play: register for one date, pay the venue by
/// GCash, and wait for the venue to confirm.
///
/// Registering holds the spot for the venue's payment hold, the same one a
/// court booking gets. Sending the receipt stops that clock and hands the
/// registration to the desk. The player is registered only once the desk
/// confirms.
/// </summary>
public interface ICustomerOpenPlayService
{
    Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> RegisterAsync(
        Guid customerUserId,
        Guid openPlayId,
        RegisterForOpenPlayRequest request,
        CancellationToken cancellationToken);

    Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> GetAsync(
        Guid customerUserId,
        Guid registrationId,
        CancellationToken cancellationToken);

    /// <summary>The player's own registrations, newest session first.</summary>
    Task<IReadOnlyCollection<OpenPlayRegistrationDetail>> ListMineAsync(
        Guid customerUserId,
        CancellationToken cancellationToken);

    Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> SendReceiptAsync(
        Guid customerUserId,
        Guid registrationId,
        OpenPlayReceiptRequest request,
        CancellationToken cancellationToken);
}
