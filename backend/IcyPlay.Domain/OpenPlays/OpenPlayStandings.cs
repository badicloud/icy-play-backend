namespace IcyPlay.Domain.OpenPlays;

/// <summary>One player's record over the matches tallied.</summary>
public sealed record OpenPlayStanding(Guid ParticipantId, int Played, int Won, int Drawn, int Lost);

/// <summary>
/// Who has won the most. A match counts for the players on court when it
/// finished — whoever came on for somebody done mid-game plays it out and
/// takes the result; whoever went off takes nothing from it. A match finished
/// with no result yet counts for nobody until the desk gives it one.
/// </summary>
public static class OpenPlayStandings
{
    /// <summary>
    /// Everyone who has played a match with a result: most wins first, then
    /// fewest losses, then most draws.
    /// </summary>
    public static IReadOnlyList<OpenPlayStanding> Tally(IEnumerable<OpenPlayMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        var records = new Dictionary<Guid, (int Played, int Won, int Drawn, int Lost)>();

        foreach (var match in matches.Where(match => match.Status == OpenPlayMatchStatus.Finished && match.Result is not null))
        {
            foreach (var slot in match.Seated)
            {
                var (played, won, drawn, lost) = records.GetValueOrDefault(slot.ParticipantId);

                records[slot.ParticipantId] = match.Result == OpenPlayMatchResult.Draw
                    ? (played + 1, won, drawn + 1, lost)
                    : match.Result == slot.Team
                        ? (played + 1, won + 1, drawn, lost)
                        : (played + 1, won, drawn, lost + 1);
            }
        }

        return Ranked(records.Select(pair =>
            new OpenPlayStanding(pair.Key, pair.Value.Played, pair.Value.Won, pair.Value.Drawn, pair.Value.Lost)));
    }

    /// <summary>Most wins first, then fewest losses, then most draws.</summary>
    public static IReadOnlyList<OpenPlayStanding> Ranked(IEnumerable<OpenPlayStanding> standings) =>
        [.. standings
            .OrderByDescending(standing => standing.Won)
            .ThenBy(standing => standing.Lost)
            .ThenByDescending(standing => standing.Drawn)];

    /// <summary>
    /// Place by wins alone: the same wins, the same place — two on 4 wins are
    /// both 1st, and the next is 3rd. No tie-breaker, as the venue wants.
    /// </summary>
    public static int RankOf(IReadOnlyCollection<int> wins, int won) =>
        1 + wins.Count(other => other > won);

    /// <summary>
    /// The best players: everyone level on the most wins — two or three of
    /// them is fine. Nobody, until somebody has won.
    /// </summary>
    public static IReadOnlyList<OpenPlayStanding> Best(IReadOnlyCollection<OpenPlayStanding> standings)
    {
        ArgumentNullException.ThrowIfNull(standings);

        var most = standings.Count == 0 ? 0 : standings.Max(standing => standing.Won);

        return most == 0 ? [] : [.. standings.Where(standing => standing.Won == most)];
    }
}
