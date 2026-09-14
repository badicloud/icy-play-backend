using FluentAssertions.Execution;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.UnitTests;

public sealed class BookableCourtTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    // One floor, sold three ways: basketball whole, volleyball whole, and
    // pickleball marked out three across. Five bookable courts on one slab of
    // concrete, which is the situation this whole rule exists for.
    //
    // A court-and-sport pair belongs to one court, so each floor gets its own
    // ids for the sports it takes.
    private static readonly Guid TheFloor = Guid.NewGuid();
    private static readonly Guid Basketball = Guid.NewGuid();
    private static readonly Guid Volleyball = Guid.NewGuid();
    private static readonly Guid Pickleball = Guid.NewGuid();

    private static readonly Guid TheOtherFloor = Guid.NewGuid();
    private static readonly Guid OtherBasketball = Guid.NewGuid();
    private static readonly Guid OtherPickleball = Guid.NewGuid();

    [Fact]
    public void Should_Block_The_Other_Sports_On_The_Floor_When_The_Court_Is_Played_Whole()
    {
        // Arrange
        var basketball = Whole(TheFloor, Basketball);

        // Act
        var blocked = new[]
        {
            Whole(TheFloor, Volleyball),
            Part(TheFloor, Pickleball, 1),
            Part(TheFloor, Pickleball, 2),
            Part(TheFloor, Pickleball, 3)
        };

        // Assert: a basketball game is played over all of that paint. Selling
        // any of these alongside it puts two games in one gym.
        blocked.Should().OnlyContain(other => basketball.ConflictsWith(other));
    }

    [Fact]
    public void Should_Leave_The_Other_Parts_Free_When_One_Part_Is_Taken()
    {
        // Arrange
        var first = Part(TheFloor, Pickleball, 1);

        // Act, Assert: three pickleball games at once is the entire point of
        // marking the floor out, so parts two and three stay sellable.
        using (new AssertionScope())
        {
            first.ConflictsWith(Part(TheFloor, Pickleball, 2)).Should().BeFalse();
            first.ConflictsWith(Part(TheFloor, Pickleball, 3)).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Block_The_Whole_Court_Sports_When_One_Part_Is_Taken()
    {
        // Arrange
        var first = Part(TheFloor, Pickleball, 1);

        // Act, Assert: the net is up across a third of the floor. Nobody is
        // playing basketball around it.
        using (new AssertionScope())
        {
            first.ConflictsWith(Whole(TheFloor, Basketball)).Should().BeTrue();
            first.ConflictsWith(Whole(TheFloor, Volleyball)).Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Block_Itself()
    {
        // Arrange
        var first = Part(TheFloor, Pickleball, 1);

        // Act, Assert: the obvious half of the rule, and the half that would
        // sell one court twice if the comparison were written backwards.
        first.ConflictsWith(Part(TheFloor, Pickleball, 1)).Should().BeTrue();
    }

    [Fact]
    public void Should_Not_Reach_Across_To_A_Different_Court()
    {
        // Arrange
        var here = Whole(TheFloor, Basketball);

        // Act, Assert: a venue with two gyms runs two games. The rule is about
        // one floor, not one sport.
        using (new AssertionScope())
        {
            here.ConflictsWith(Whole(TheOtherFloor, OtherBasketball)).Should().BeFalse();
            here.ConflictsWith(Part(TheOtherFloor, OtherPickleball, 1)).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Keep_Its_Identity_When_A_Retired_Part_Is_Marked_Out_Again()
    {
        // Arrange: the floor goes from three parts down to two.
        var third = Part(TheFloor, Pickleball, 3);
        var id = third.Id;
        third.Retire(Now);

        // Act: and back to three a month later.
        third.Reinstate(Now.AddMonths(1));

        // Assert: the same row, so a booking that outlived the gap still
        // resolves to the court it was taken against.
        using (new AssertionScope())
        {
            third.Id.Should().Be(id);
            third.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public void Should_Become_Divided_When_A_Whole_Court_Is_Marked_Out()
    {
        // Arrange: played whole, then the floor is marked for three.
        var sut = Whole(TheFloor, Pickleball);

        // Act
        sut.SetKind(BookableCourtKind.For(3), Now);

        // Assert: part one of three is the row it always was, described
        // differently. Nothing else on the row could say so — a whole court and
        // part one both carry number one.
        using (new AssertionScope())
        {
            sut.Kind.Should().Be(BookableCourtKind.Divided);
            sut.IsDivided.Should().BeTrue();
            sut.DivisionNumber.Should().Be(1);
        }
    }

    [Theory]
    [InlineData(1, BookableCourtKind.Whole)]
    [InlineData(2, BookableCourtKind.Divided)]
    [InlineData(3, BookableCourtKind.Divided)]
    public void Should_Call_A_Court_Divided_Only_When_It_Makes_More_Than_One(
        int divisions,
        string expected) =>
        // Assert: one of one is a court played whole, whatever the form says.
        BookableCourtKind.For(divisions).Should().Be(expected);

    private static BookableCourt Whole(Guid courtId, Guid courtSportId) =>
        new(courtSportId, courtId, 1, BookableCourtKind.Whole, Now);

    private static BookableCourt Part(Guid courtId, Guid courtSportId, int number) =>
        new(courtSportId, courtId, number, BookableCourtKind.Divided, Now);
}
