namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// The letters an open play registration sends. None of them throws: the
/// registration is already saved, and a mail provider having a bad afternoon
/// must not report it as failed. The desk still sees it in its queue.
/// </summary>
public interface IOpenPlayNotifier
{
    /// <summary>
    /// The receipt is in: to the player, that the venue is checking it and
    /// they are not registered yet; to everybody on the venue's desk, that
    /// there is one to check.
    /// </summary>
    Task PaymentSubmittedAsync(Guid registrationId, CancellationToken cancellationToken);

    /// <summary>To the player: the venue confirmed the payment, and they are registered.</summary>
    Task ConfirmedAsync(Guid registrationId, CancellationToken cancellationToken);

    /// <summary>To the player: the venue turned the payment down, why, and how to reach them.</summary>
    Task DeclinedAsync(Guid registrationId, CancellationToken cancellationToken);
}
