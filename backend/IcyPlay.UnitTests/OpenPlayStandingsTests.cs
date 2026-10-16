using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

/// <summary>
/// Who has won the most, on two to a team: players 1–2 against 3–4, then
/// the line comes round.
/// </summary>
public sealed class OpenPlayStandingsTests
{
    private const int Two = 2;

    private static readonly Guid Desk = TestIds.For("desk");

    [Fact]
    public void Should_Rank_The_Winners_First_When_A_Match_Has_A_Result()
    {
        // Arrange
        var session = NewSession(6);
        Play(session, 1, OpenPlayMatchResult.TeamA);

        // Act
        var standings = OpenPlayStandings.Tally(session.Matches);

        // Assert
        using (new AssertionScope())
        {
            standings.Should().HaveCount(4);
            standings.Take(2).Select(standing => Number(session, standing)).Should().BeEquivalentTo([1, 2]);
            standings.Take(2).Should().OnlyContain(standing => standing.Played == 1 && standing.Won == 1);
            standings.Skip(2).Select(standing => Number(session, standing)).Should().BeEquivalentTo([3, 4]);
            standings.Skip(2).Should().OnlyContain(standing => standing.Lost == 1);
        }
    }

    [Fact]
    public void Should_Count_A_Draw_For_Both_Teams_When_It_Ends_Level()
    {
        // Arrange
        var session = NewSession(4);
        Play(session, 1, OpenPlayMatchResult.Draw);

        // Act
        var standings = OpenPlayStandings.Tally(session.Matches);

        // Assert
        using (new AssertionScope())
        {
            standings.Should().HaveCount(4);
            standings.Should().OnlyContain(standing => standing.Drawn == 1 && standing.Won == 0 && standing.Lost == 0);
            OpenPlayStandings.Best(standings).Should().BeEmpty();
        }
    }

    [Fact]
    public void Should_Give_The_Result_To_Whoever_Finished_The_Match_When_A_Player_Went_Off_Mid_Game()
    {
        // Arrange: player 1 goes off; 5, at the front of the line, comes on.
        var session = NewSession(6);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Two, evenIfShort: false, TestTimes.UtcNow);
        OpenPlayLineup.Done(session, Player(session, 1), Two, TestTimes.UtcNow.AddMinutes(3));
        OpenPlayLineup.Finish(session, Match(session, 1), Desk, Two, OpenPlayMatchResult.TeamA, null, null, TestTimes.UtcNow.AddMinutes(10));

        // Act
        var standings = OpenPlayStandings.Tally(session.Matches);

        // Assert
        using (new AssertionScope())
        {
            standings.Select(standing => Number(session, standing)).Should().NotContain(1);
            standings.Single(standing => Number(session, standing) == 5).Won.Should().Be(1);
        }
    }

    [Fact]
    public void Should_Leave_Out_A_Match_When_It_Has_No_Result()
    {
        // Arrange: a match still on court.
        var session = NewSession(4);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Two, evenIfShort: false, TestTimes.UtcNow);

        // Act
        var standings = OpenPlayStandings.Tally(session.Matches);

        // Assert
        standings.Should().BeEmpty();
    }

    [Fact]
    public void Should_Share_A_Place_And_Name_Every_Best_Player_When_Wins_Are_Level()
    {
        // Arrange: six players, three matches, Team A each time: 1–2 beat 3–4, 5–6 beat 1–2, 3–4 beat 5–6.
        var session = NewSession(6);
        Play(session, 1, OpenPlayMatchResult.TeamA);
        Play(session, 2, OpenPlayMatchResult.TeamA);
        Play(session, 3, OpenPlayMatchResult.TeamA);

        // Act
        var standings = OpenPlayStandings.Tally(session.Matches);
        var wins = standings.Select(standing => standing.Won).ToList();

        // Assert
        using (new AssertionScope())
        {
            standings.Should().HaveCount(6);
            standings.Should().OnlyContain(standing => standing.Won == 1 && standing.Lost == 1);
            standings.Should().OnlyContain(standing => OpenPlayStandings.RankOf(wins, standing.Won) == 1);
            OpenPlayStandings.Best(standings).Should().HaveCount(6);
        }
    }

    [Fact]
    public void Should_Place_Players_By_Wins_Alone_When_Ranking()
    {
        // Arrange
        int[] wins = [4, 4, 2, 1];

        // Act
        var places = wins.Select(won => OpenPlayStandings.RankOf(wins, won)).ToList();

        // Assert
        places.Should().Equal(1, 1, 3, 4);
    }

    [Theory]
    [InlineData("A", null, null, true)]
    [InlineData("B", 9, 11, true)]
    [InlineData("Draw", 10, 10, true)]
    [InlineData("A", 9, 11, false)]
    [InlineData("Draw", 11, 9, false)]
    [InlineData("A", 11, null, false)]
    [InlineData("A", 1000, 2, false)]
    [InlineData("A", 3, -1, false)]
    [InlineData("C", null, null, false)]
    [InlineData(null, null, null, false)]
    public void Should_Hold_Only_When_The_Score_Agrees_With_Who_Won(string? result, int? scoreA, int? scoreB, bool holds)
    {
        // Act
        var actual = OpenPlayMatchResult.Holds(result, scoreA, scoreB);

        // Assert
        actual.Should().Be(holds);
    }

    /// <summary>A session with the given number of players checked in, a second apart.</summary>
    private static OpenPlaySession NewSession(int players)
    {
        var session = new OpenPlaySession(TestIds.For("open-play"), OpenPlayBuilder.FirstSaturday, TestTimes.UtcNow);

        for (var number = 1; number <= players; number++)
        {
            OpenPlayLineup.Join(session, TestIds.For("player", number), null, Two, TestTimes.UtcNow.AddSeconds(number));
        }

        return session;
    }

    /// <summary>Starts and finishes a match with the given result, ten minutes on per match.</summary>
    private static void Play(OpenPlaySession session, int number, string result)
    {
        var at = TestTimes.UtcNow.AddMinutes(number * 10);
        OpenPlayLineup.Start(session, Match(session, number), Desk, Two, evenIfShort: false, at);
        OpenPlayLineup.Finish(session, Match(session, number), Desk, Two, result, null, null, at.AddMinutes(8));
    }

    private static OpenPlayParticipant Player(OpenPlaySession session, int number) =>
        session.Participants.Single(participant => participant.RegistrationId == TestIds.For("player", number));

    private static OpenPlayMatch Match(OpenPlaySession session, int number) =>
        session.Matches.Single(match => match.Number == number);

    /// <summary>The number of the player a standing is for.</summary>
    private static int Number(OpenPlaySession session, OpenPlayStanding standing)
    {
        var participant = session.Participants.Single(candidate => candidate.Id == standing.ParticipantId);

        for (var number = 1; number <= 100; number++)
        {
            if (participant.RegistrationId == TestIds.For("player", number))
            {
                return number;
            }
        }

        throw new InvalidOperationException("Not one of the numbered players.");
    }
}
