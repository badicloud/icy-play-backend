using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Loads a session's line and matches, lets <see cref="OpenPlayLineup"/> change
/// them, and saves. Two desks pressing at once can both make "Match 4"; the
/// unique number turns the second away, and this reads the session again and
/// has another go — every change here is safe to repeat.
/// </summary>
internal static class SessionLineups
{
    private const int Tries = 3;

    public static Task<OpenPlaySession> LoadAsync(AppDbContext db, Guid sessionId, CancellationToken ct) =>
        db.OpenPlaySessions
            .Include(session => session.Participants)
            .Include(session => session.Matches)
                .ThenInclude(match => match.Slots)
            .SingleAsync(session => session.Id == sessionId, ct);

    /// <summary>
    /// Applies a change to the session's lineup and saves it. Answers what the
    /// change answered. The caller saves its own changes first: a retry starts
    /// from a clean context, and anything still pending would be lost with it.
    /// </summary>
    public static async Task<T> ApplyAsync<T>(
        AppDbContext db,
        Guid sessionId,
        Func<OpenPlaySession, T> change,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var session = await LoadAsync(db, sessionId, ct);
            var answer = change(session);

            try
            {
                await db.SaveChangesAsync(ct);

                return answer;
            }
            catch (DbUpdateException) when (attempt < Tries)
            {
                // Somebody else changed the same session in the same moment.
                // Start again from what is saved.
                db.ChangeTracker.Clear();
            }
        }
    }
}
