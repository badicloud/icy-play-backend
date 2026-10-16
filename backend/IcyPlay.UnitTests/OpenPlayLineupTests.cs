using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

/// <summary>
/// The rules the desk runs matches by, on the venue's own example: basketball,
/// five a team, fifteen players checked in.
/// </summary>
public sealed class OpenPlayLineupTests
{
    private const int Five = 5;

    private static readonly Guid Desk = TestIds.For("desk");

    [Fact]
    public void Should_Fill_Team_A_Then_Team_B_In_Check_In_Order_When_Players_Check_In()
    {
        // Arrange
        var session = NewSession();

        // Act
        CheckIn(session, 1, 15);

        // Assert
        var first = Match(session, 1);
        var second = Match(session, 2);

        using (new AssertionScope())
        {
            session.Matches.Should().HaveCount(2);
            first.Status.Should().Be(OpenPlayMatchStatus.Ready);
            Team(session, first, OpenPlayTeam.A).Should().Equal(1, 2, 3, 4, 5);
            Team(session, first, OpenPlayTeam.B).Should().Equal(6, 7, 8, 9, 10);
            second.Status.Should().Be(OpenPlayMatchStatus.Forming);
            Team(session, second, OpenPlayTeam.A).Should().Equal(11, 12, 13, 14, 15);
            Team(session, second, OpenPlayTeam.B).Should().BeEmpty();
        }
    }

    [Fact]
    public void Should_Take_The_First_Open_Seat_When_Another_Player_Checks_In()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 15);

        // Act
        CheckIn(session, 16, 16);

        // Assert
        Team(session, Match(session, 2), OpenPlayTeam.B).Should().Equal(16);
    }

    [Fact]
    public void Should_Move_Everyone_Behind_Up_When_A_Player_Is_Done_Before_Their_Match()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 15);

        // Act
        OpenPlayLineup.Done(session, Player(session, 3), Five, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            Team(session, Match(session, 1), OpenPlayTeam.A).Should().Equal(1, 2, 4, 5, 6);
            Team(session, Match(session, 1), OpenPlayTeam.B).Should().Equal(7, 8, 9, 10, 11);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(12, 13, 14, 15);
            Player(session, 3).Status.Should().Be(OpenPlayParticipantStatus.Done);
        }
    }

    [Fact]
    public void Should_Send_Players_To_The_End_Of_The_Line_When_Their_Match_Finishes()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 15);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);

        // Act
        var refusal = OpenPlayLineup.Finish(session, Match(session, 1), Desk, Five, OpenPlayMatchResult.TeamA, 21, 17, TestTimes.UtcNow.AddMinutes(12));

        // Assert
        using (new AssertionScope())
        {
            refusal.Should().BeNull();
            Match(session, 1).Status.Should().Be(OpenPlayMatchStatus.Finished);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(11, 12, 13, 14, 15);
            Team(session, Match(session, 2), OpenPlayTeam.B).Should().Equal(1, 2, 3, 4, 5);
            Match(session, 2).Status.Should().Be(OpenPlayMatchStatus.Ready);
            Team(session, Match(session, 3), OpenPlayTeam.A).Should().Equal(6, 7, 8, 9, 10);
        }
    }

    [Fact]
    public void Should_Put_The_Front_Of_The_Line_In_When_A_Player_Is_Done_Mid_Game()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 15);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);

        // Act
        OpenPlayLineup.Done(session, Player(session, 3), Five, TestTimes.UtcNow.AddMinutes(5));

        // Assert
        using (new AssertionScope())
        {
            Team(session, Match(session, 1), OpenPlayTeam.A).Should().Equal(1, 2, 11, 4, 5);
            Match(session, 1).Slots.Single(slot => slot.ParticipantId == Player(session, 3).Id).LeftAt.Should().NotBeNull();
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(12, 13, 14, 15);
        }
    }

    [Fact]
    public void Should_Refuse_To_Start_When_It_Is_Not_The_Next_Match_Or_The_Court_Is_Busy()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 20);

        // Act
        var outOfTurn = OpenPlayLineup.Start(session, Match(session, 2), Desk, Five, evenIfShort: false, TestTimes.UtcNow);
        var first = OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);
        var whileBusy = OpenPlayLineup.Start(session, Match(session, 2), Desk, Five, evenIfShort: false, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            outOfTurn.Should().Be(LineupRefusal.NotNext);
            first.Should().BeNull();
            whileBusy.Should().Be(LineupRefusal.CourtBusy);
            Match(session, 1).Status.Should().Be(OpenPlayMatchStatus.Playing);
        }
    }

    [Fact]
    public void Should_Start_A_Short_Match_Only_When_The_Desk_Says_So()
    {
        // Arrange: eight players, evened out at four a side.
        var session = NewSession();
        CheckIn(session, 1, 8);
        OpenPlayLineup.MoveToTeam(session, Player(session, 5), OpenPlayTeam.B, Five, TestTimes.UtcNow);

        // Act
        var refused = OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);
        var anyway = OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: true, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            refused.Should().Be(LineupRefusal.Short);
            anyway.Should().BeNull();
            Match(session, 1).IsPlaying.Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Refuse_To_Start_When_The_Teams_Are_Uneven_Even_If_The_Desk_Says_So()
    {
        // Arrange: nine players, five against four.
        var session = NewSession();
        CheckIn(session, 1, 9);

        // Act
        var refusal = OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: true, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            refusal.Should().Be(LineupRefusal.Uneven);
            Match(session, 1).HasStarted.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Refuse_To_Start_When_A_Team_Has_Nobody()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 4);

        // Act
        var refusal = OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: true, TestTimes.UtcNow);

        // Assert
        refusal.Should().Be(LineupRefusal.TeamEmpty);
    }

    [Fact]
    public void Should_Forget_A_Player_Who_Never_Played_When_Their_Check_In_Is_Undone()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 12);

        // Act
        var forgotten = OpenPlayLineup.Withdraw(session, Player(session, 12), Five, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            forgotten.Should().BeTrue();
            session.Participants.Should().HaveCount(11);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(11);
        }
    }

    [Fact]
    public void Should_Keep_A_Player_Who_Played_On_Record_When_Their_Check_In_Is_Undone()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 10);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);
        OpenPlayLineup.Finish(session, Match(session, 1), Desk, Five, OpenPlayMatchResult.Draw, null, null, TestTimes.UtcNow.AddMinutes(10));

        // Act
        var forgotten = OpenPlayLineup.Withdraw(session, Player(session, 1), Five, TestTimes.UtcNow.AddMinutes(11));

        // Assert
        using (new AssertionScope())
        {
            forgotten.Should().BeFalse();
            Player(session, 1).Status.Should().Be(OpenPlayParticipantStatus.Done);
        }
    }

    [Fact]
    public void Should_Go_To_The_End_Of_The_Line_When_A_Done_Player_Is_Back_In()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 12);
        OpenPlayLineup.Done(session, Player(session, 1), Five, TestTimes.UtcNow);

        // Act
        OpenPlayLineup.BackIn(session, Player(session, 1), Five, TestTimes.UtcNow.AddMinutes(1));

        // Assert
        Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(12, 1);
    }

    [Fact]
    public void Should_Take_No_New_Player_When_A_Match_Is_Under_Way()
    {
        // Arrange: a short match, four a side, started; then a latecomer.
        var session = NewSession();
        CheckIn(session, 1, 8);
        OpenPlayLineup.MoveToTeam(session, Player(session, 5), OpenPlayTeam.B, Five, TestTimes.UtcNow);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: true, TestTimes.UtcNow);

        // Act
        CheckIn(session, 9, 9);

        // Assert
        using (new AssertionScope())
        {
            Team(session, Match(session, 1), OpenPlayTeam.B).Should().Equal(5, 6, 7, 8);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(9);
        }
    }

    [Fact]
    public void Should_Keep_Arrival_Order_When_Players_Check_In_At_The_Same_Instant()
    {
        // Arrange
        var session = NewSession();

        // Act: one clock reading for all four.
        for (var number = 1; number <= 4; number++)
        {
            OpenPlayLineup.Join(session, TestIds.For("player", number), null, 2, TestTimes.UtcNow);
        }

        // Assert
        using (new AssertionScope())
        {
            Team(session, Match(session, 1), OpenPlayTeam.A).Should().Equal(1, 2);
            Team(session, Match(session, 1), OpenPlayTeam.B).Should().Equal(3, 4);
        }
    }

    [Fact]
    public void Should_Refuse_To_Finish_When_The_Score_Says_The_Other_Team_Won()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 10);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);

        // Act
        var refusal = OpenPlayLineup.Finish(session, Match(session, 1), Desk, Five, OpenPlayMatchResult.TeamA, 15, 21, TestTimes.UtcNow.AddMinutes(12));

        // Assert
        using (new AssertionScope())
        {
            refusal.Should().Be(LineupRefusal.ResultDoesNotHold);
            Match(session, 1).IsPlaying.Should().BeTrue();
            Match(session, 1).Result.Should().BeNull();
        }
    }

    [Fact]
    public void Should_Change_Only_The_Result_When_A_Finished_Result_Is_Put_Right()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 10);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);
        OpenPlayLineup.Finish(session, Match(session, 1), Desk, Five, OpenPlayMatchResult.TeamA, null, null, TestTimes.UtcNow.AddMinutes(12));
        var lineBefore = OpenPlayLineup.Line(session).Select(Number).ToList();

        // Act
        var refusal = OpenPlayLineup.CorrectResult(Match(session, 1), OpenPlayMatchResult.TeamB, 18, 21, TestTimes.UtcNow.AddMinutes(13));

        // Assert
        using (new AssertionScope())
        {
            refusal.Should().BeNull();
            Match(session, 1).Result.Should().Be(OpenPlayMatchResult.TeamB);
            Match(session, 1).ScoreA.Should().Be(18);
            Match(session, 1).ScoreB.Should().Be(21);
            OpenPlayLineup.Line(session).Select(Number).Should().Equal(lineBefore);
        }
    }

    [Fact]
    public void Should_Refuse_To_Put_A_Result_Right_When_The_Match_Has_Not_Finished()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 10);

        // Act
        var refusal = OpenPlayLineup.CorrectResult(Match(session, 1), OpenPlayMatchResult.Draw, null, null, TestTimes.UtcNow);

        // Assert
        refusal.Should().Be(LineupRefusal.NotFinished);
    }

    [Fact]
    public void Should_Seat_The_Pick_And_Send_The_Player_To_The_End_When_They_Play_The_Next_Match()
    {
        // Arrange: 1–10 have played Match 1 and make up Match 2; 11–15 arrive
        // after and make up Match 3.
        var session = NewSession();
        CheckIn(session, 1, 10);
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow);
        OpenPlayLineup.Finish(session, Match(session, 1), Desk, Five, OpenPlayMatchResult.TeamA, null, null, TestTimes.UtcNow.AddMinutes(10));
        CheckIn(session, 11, 15);

        // Act: 6 plays the next match; the desk picks 13 for the seat.
        var refusal = OpenPlayLineup.PlayNextMatch(session, Player(session, 6), Player(session, 13), Five, TestTimes.UtcNow.AddMinutes(11));

        // Assert
        using (new AssertionScope())
        {
            refusal.Should().BeNull();
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(1, 2, 3, 4, 5);
            Team(session, Match(session, 2), OpenPlayTeam.B).Should().Equal(13, 7, 8, 9, 10);
            Team(session, Match(session, 3), OpenPlayTeam.A).Should().Equal(11, 12, 14, 15, 6);
        }
    }

    [Fact]
    public void Should_Refuse_To_Play_The_Next_Match_When_The_Player_Is_Not_In_It()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 14);

        // Act
        var refusal = OpenPlayLineup.PlayNextMatch(session, Player(session, 14), Player(session, 12), Five, TestTimes.UtcNow.AddMinutes(1));

        // Assert
        using (new AssertionScope())
        {
            refusal.Should().Be(LineupRefusal.NotInNextMatch);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(11, 12, 13, 14);
        }
    }

    [Fact]
    public void Should_Refuse_To_Play_The_Next_Match_When_The_Pick_Is_In_It_Too()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 14);

        // Act
        var refusal = OpenPlayLineup.PlayNextMatch(session, Player(session, 3), Player(session, 7), Five, TestTimes.UtcNow.AddMinutes(1));

        // Assert
        refusal.Should().Be(LineupRefusal.ReplacementNotFurtherBack);
    }

    [Fact]
    public void Should_Put_The_Player_On_The_Other_Team_When_The_Desk_Moves_Them()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 14);

        // Act
        var refusal = OpenPlayLineup.MoveToTeam(session, Player(session, 3), OpenPlayTeam.B, Five, TestTimes.UtcNow.AddMinutes(1));

        // Assert: 6 against 4, and short until somebody comes back across.
        using (new AssertionScope())
        {
            refusal.Should().BeNull();
            Team(session, Match(session, 1), OpenPlayTeam.A).Should().Equal(1, 2, 4, 5);
            Team(session, Match(session, 1), OpenPlayTeam.B).Should().BeEquivalentTo([3, 6, 7, 8, 9, 10]);
            Match(session, 1).Status.Should().Be(OpenPlayMatchStatus.Forming);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(11, 12, 13, 14);
        }
    }

    [Fact]
    public void Should_Even_The_Teams_Again_When_A_Player_Is_Moved_Back_The_Other_Way()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 14);
        OpenPlayLineup.MoveToTeam(session, Player(session, 3), OpenPlayTeam.B, Five, TestTimes.UtcNow.AddMinutes(1));

        // Act
        OpenPlayLineup.MoveToTeam(session, Player(session, 8), OpenPlayTeam.A, Five, TestTimes.UtcNow.AddMinutes(2));
        OpenPlayLineup.Join(session, TestIds.For("player", 15), null, Five, TestTimes.UtcNow.AddMinutes(3));

        // Assert: still as the desk set them after another check-in.
        using (new AssertionScope())
        {
            Team(session, Match(session, 1), OpenPlayTeam.A).Should().BeEquivalentTo([1, 2, 4, 5, 8]);
            Team(session, Match(session, 1), OpenPlayTeam.B).Should().BeEquivalentTo([3, 6, 7, 9, 10]);
            Match(session, 1).Status.Should().Be(OpenPlayMatchStatus.Ready);
        }
    }

    [Fact]
    public void Should_Forget_The_Move_When_The_Match_Has_Been_Played()
    {
        // Arrange: 3 moved to Team B and 8 to Team A, then Match 1 played.
        var session = NewSession();
        CheckIn(session, 1, 10);
        OpenPlayLineup.MoveToTeam(session, Player(session, 3), OpenPlayTeam.B, Five, TestTimes.UtcNow.AddMinutes(1));
        OpenPlayLineup.MoveToTeam(session, Player(session, 8), OpenPlayTeam.A, Five, TestTimes.UtcNow.AddMinutes(1));
        OpenPlayLineup.Start(session, Match(session, 1), Desk, Five, evenIfShort: false, TestTimes.UtcNow.AddMinutes(2));

        // Act
        OpenPlayLineup.Finish(session, Match(session, 1), Desk, Five, OpenPlayMatchResult.Draw, null, null, TestTimes.UtcNow.AddMinutes(12));

        // Assert: back to the teams their place in line gives them.
        using (new AssertionScope())
        {
            session.Participants.Should().OnlyContain(participant => participant.PinnedTeam == null);
            Team(session, Match(session, 2), OpenPlayTeam.A).Should().Equal(1, 2, 4, 5, 8);
        }
    }

    [Fact]
    public void Should_Refuse_To_Move_When_The_Player_Is_Already_On_That_Team_Or_Not_In_The_Next_Match()
    {
        // Arrange
        var session = NewSession();
        CheckIn(session, 1, 14);

        // Act
        var sameTeam = OpenPlayLineup.MoveToTeam(session, Player(session, 1), OpenPlayTeam.A, Five, TestTimes.UtcNow.AddMinutes(1));
        var later = OpenPlayLineup.MoveToTeam(session, Player(session, 12), OpenPlayTeam.B, Five, TestTimes.UtcNow.AddMinutes(1));

        // Assert
        using (new AssertionScope())
        {
            sameTeam.Should().Be(LineupRefusal.SameTeam);
            later.Should().Be(LineupRefusal.NotInNextMatch);
            Team(session, Match(session, 1), OpenPlayTeam.A).Should().Equal(1, 2, 3, 4, 5);
        }
    }

    private static OpenPlaySession NewSession() =>
        new(TestIds.For("open-play"), OpenPlayBuilder.FirstSaturday, TestTimes.UtcNow);

    /// <summary>Players numbered first to last check in a second apart, in that order.</summary>
    private static void CheckIn(OpenPlaySession session, int first, int last)
    {
        for (var number = first; number <= last; number++)
        {
            OpenPlayLineup.Join(session, TestIds.For("player", number), null, Five, TestTimes.UtcNow.AddSeconds(number));
        }
    }

    private static OpenPlayParticipant Player(OpenPlaySession session, int number) =>
        session.Participants.Single(participant => participant.RegistrationId == TestIds.For("player", number));

    private static OpenPlayMatch Match(OpenPlaySession session, int number) =>
        session.Matches.Single(match => match.Number == number);

    /// <summary>The player numbers seated on one team, seat by seat.</summary>
    private static List<int> Team(OpenPlaySession session, OpenPlayMatch match, string team) =>
        match.Seated
            .Where(slot => slot.Team == team)
            .OrderBy(slot => slot.Position)
            .Select(slot => Number(session.Participants.Single(participant => participant.Id == slot.ParticipantId)))
            .ToList();

    private static int Number(OpenPlayParticipant participant)
    {
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
