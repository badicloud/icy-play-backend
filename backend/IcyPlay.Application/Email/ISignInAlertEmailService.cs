namespace IcyPlay.Application.Email;

/// <summary>
/// Tells somebody their account was signed in to from a device we have not
/// seen before.
///
/// This is how account theft is actually noticed. The list of signed-in
/// devices only works for somebody who thinks to look; a letter arrives
/// whether or not they were thinking about it.
///
/// Deliberately not every sign-in. Somebody who signs in daily from the same
/// laptop would get a daily letter, and within a week they would stop reading
/// them — which costs the one letter that mattered.
/// </summary>
public interface ISignInAlertEmailService
{
    /// <summary>
    /// Sends the alert if this device is new to the account, and does nothing
    /// if it is not.
    ///
    /// Never throws. A sign-in that has already happened must not be reported
    /// as failed because a mail provider was down.
    /// </summary>
    Task SendIfNewDeviceAsync(
        Guid userId,
        string recipientEmail,
        string recipientName,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken);
}
