using IcyPlay.Application.Audit;

namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// Checking players in at an open play session: by scanning their pass, or by
/// tapping their name when the phone is flat. Owners and attendants, on the
/// venues they work, while the session's check-in window is open on the
/// venue's clock.
/// </summary>
public interface IDeskCheckInService
{
    /// <summary>One session's roster, for the scanner and for the wide screen.</summary>
    Task<OpenPlayRegistrationResult<CheckInRoster>> RosterAsync(
        Guid userId,
        Guid openPlayId,
        DateOnly date,
        CancellationToken cancellationToken);

    /// <summary>
    /// What the camera read. Answers with an outcome rather than refusing, so
    /// the desk always has one line to read out: checked in, already in, not on
    /// this session, not paid yet, not a pass.
    /// </summary>
    Task<OpenPlayRegistrationResult<CheckInScanResult>> ScanAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        CheckInScanRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Puts a player into the session for free, to make up a game: up to the
    /// open play's free spots, while there is a spot, before the session ends.
    /// Checked in at once when the check-in window is open, since the desk is
    /// adding somebody standing in front of it.
    /// </summary>
    Task<OpenPlayRegistrationResult<CheckInRoster>> AddFreePlayerAsync(
        AuditActor actor,
        Guid openPlayId,
        DateOnly date,
        AddFreePlayerRequest request,
        CancellationToken cancellationToken);

    /// <summary>Takes a free player back out, before the session ends. Their spot opens again.</summary>
    Task<OpenPlayRegistrationResult<CheckInRoster>> RemoveFreePlayerAsync(
        AuditActor actor,
        Guid freePlayerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a registered player — or a free one, by their own id — in by
    /// hand, from the roster. Needs the venue's check-in code, since there is
    /// no QR to vouch for them.
    /// </summary>
    Task<OpenPlayRegistrationResult<CheckInRoster>> CheckInAsync(
        AuditActor actor,
        Guid registrationId,
        CheckInCodeRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Takes back a check-in made by mistake. Needs the venue's code too: it
    /// makes the player's QR good again.
    /// </summary>
    Task<OpenPlayRegistrationResult<CheckInRoster>> UndoAsync(
        AuditActor actor,
        Guid registrationId,
        CheckInCodeRequest request,
        CancellationToken cancellationToken);
}
