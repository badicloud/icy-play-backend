using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Giving a session's best players their reward when it ends, and taking it
/// back when it is reopened. Only players who registered, with an account to
/// put it on: a free player has none. Saves nothing; the caller does.
/// </summary>
internal static class OpenPlayRewards
{
    /// <summary>
    /// Gives each best player of the session the open play's reward: a voucher
    /// each, or the next claim stub while there are any left.
    /// </summary>
    /// <returns>How many were given, and how many best players went without because the prizes ran out.</returns>
    public static async Task<(int Given, int RanOut)> GiveAsync(
        AppDbContext db,
        OpenPlay openPlay,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (openPlay.Reward is not { } reward)
        {
            return (0, 0);
        }

        var players = await BestPlayersAsync(db, sessionId, ct);

        if (players.Count == 0)
        {
            return (0, 0);
        }

        if (reward.Kind == OpenPlayRewardKind.Voucher)
        {
            foreach (var player in players)
            {
                db.OpenPlayVouchers.Add(new OpenPlayVoucher(openPlay.FacilityId, player, openPlay.Id, sessionId, reward, now));
            }

            return (players.Count, 0);
        }

        var stubs = await db.OpenPlayClaimStubs
            .Where(stub => stub.OpenPlayId == openPlay.Id && stub.CustomerUserId == null)
            .OrderBy(stub => stub.CreatedAt)
            .ThenBy(stub => stub.Code)
            .Take(players.Count)
            .ToListAsync(ct);

        for (var index = 0; index < stubs.Count; index++)
        {
            stubs[index].Assign(players[index], sessionId, reward.ValidDays, now);
        }

        return (stubs.Count, players.Count - stubs.Count);
    }

    /// <summary>
    /// Takes back what the session gave, when it is reopened: vouchers not
    /// used, and stubs not claimed, which go back to the prizes still to give.
    /// </summary>
    /// <returns>How many were taken back.</returns>
    public static async Task<int> TakeBackAsync(AppDbContext db, Guid sessionId, DateTimeOffset now, CancellationToken ct)
    {
        var vouchers = await db.OpenPlayVouchers.Where(voucher => voucher.SessionId == sessionId).ToListAsync(ct);
        var stubs = await db.OpenPlayClaimStubs.Where(stub => stub.SessionId == sessionId).ToListAsync(ct);

        return vouchers.Count(voucher => voucher.Revoke(now)) + stubs.Count(stub => stub.Unassign(now));
    }

    /// <summary>The accounts of the session's best players — everyone level on the most wins — who registered.</summary>
    private static async Task<List<Guid>> BestPlayersAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var matches = await db.OpenPlayMatches
            .AsNoTracking()
            .Include(match => match.Slots)
            .Where(match => match.SessionId == sessionId)
            .ToListAsync(ct);

        var best = OpenPlayStandings.Best(OpenPlayStandings.Tally(matches))
            .Select(standing => standing.ParticipantId)
            .ToList();

        // In the order they first arrived, so the first prizes go to the first there.
        var rows = await db.OpenPlayParticipants
            .AsNoTracking()
            .Where(participant => best.Contains(participant.Id) && participant.RegistrationId != null)
            .Join(db.OpenPlayRegistrations, participant => participant.RegistrationId, registration => registration.Id,
                (participant, registration) => new { registration.CustomerUserId, participant.CreatedAt })
            .ToListAsync(ct);

        return [.. rows.OrderBy(row => row.CreatedAt).Select(row => row.CustomerUserId).Distinct()];
    }
}
