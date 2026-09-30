using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// One date of an <see cref="OpenPlay"/>, created the first time something
/// happens on that date: a player registers, or the desk cancels it. A date
/// with no row still runs, and still blocks the court.
///
/// A cancelled session releases the court. The date goes back on sale for
/// regular bookings, and nobody can register for it.
/// </summary>
public sealed class OpenPlaySession : Entity
{
    private OpenPlaySession()
    {
    }

    public OpenPlaySession(Guid openPlayId, DateOnly date, DateTimeOffset createdAt)
    {
        OpenPlayId = openPlayId;
        Date = date;
        CreatedAt = createdAt;
    }

    public Guid OpenPlayId
    {
        get; private set;
    }
    public OpenPlay OpenPlay { get; private set; } = null!;

    public DateOnly Date
    {
        get; private set;
    }

    public DateTimeOffset? CancelledAt
    {
        get; private set;
    }

    public Guid? CancelledByUserId
    {
        get; private set;
    }

    /// <summary>Required on a cancel, because this is what the registered players are told.</summary>
    public string? CancellationReason
    {
        get; private set;
    }

    public ICollection<OpenPlayRegistration> Registrations { get; private set; } = [];

    public bool IsCancelled => CancelledAt is not null;

    /// <summary>Players holding a spot at this moment: paid, waiting on the desk, or inside their hold.</summary>
    public int SpotsTakenAt(DateTimeOffset moment) =>
        Registrations.Count(registration => registration.HoldsSpotAt(moment));

    public int SpotsLeftAt(DateTimeOffset moment, int maxPlayers) =>
        Math.Max(0, maxPlayers - SpotsTakenAt(moment));

    /// <summary>
    /// Cancels the session and every registration on it. Refunds are the desk's
    /// call and happen outside the app, so this only records the cancel.
    /// </summary>
    public void Cancel(string reason, Guid cancelledByUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancelling a session needs a reason.", nameof(reason));
        }

        if (IsCancelled)
        {
            return;
        }

        CancelledAt = now;
        CancelledByUserId = cancelledByUserId;
        CancellationReason = reason.Trim();
        UpdatedAt = now;

        foreach (var registration in Registrations)
        {
            registration.Cancel(OpenPlayRegistration.SessionCancelledReason, now);
        }
    }
}
