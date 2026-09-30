using FluentAssertions.Execution;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class OpenPlayTests
{
    private static readonly DateOnly Saturday = OpenPlayBuilder.FirstSaturday;
    private static readonly DateOnly Sunday = Saturday.AddDays(1);

    [Fact]
    public void Should_Run_On_A_Checked_Weekday_When_The_Date_Is_Inside_The_Range()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder()
            .OnDays(OpenPlayDays.Saturday | OpenPlayDays.Wednesday)
            .Build();

        // Act
        var runsOnSaturday = openPlay.RunsOn(Saturday.AddDays(7));
        var runsOnWednesday = openPlay.RunsOn(Saturday.AddDays(4));
        var runsOnSunday = openPlay.RunsOn(Sunday);

        // Assert
        using (new AssertionScope())
        {
            runsOnSaturday.Should().BeTrue();
            runsOnWednesday.Should().BeTrue();
            runsOnSunday.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Not_Run_When_The_Date_Is_Before_The_Start_Or_After_The_End()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder()
            .Running(Saturday, Saturday.AddDays(14))
            .Build();

        // Act
        var before = openPlay.RunsOn(Saturday.AddDays(-7));
        var last = openPlay.RunsOn(Saturday.AddDays(14));
        var after = openPlay.RunsOn(Saturday.AddDays(21));

        // Assert
        using (new AssertionScope())
        {
            before.Should().BeFalse();
            last.Should().BeTrue();
            after.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Run_Indefinitely_When_There_Is_No_End_Date()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Running(Saturday, null).Build();

        // Act
        var runs = openPlay.RunsOn(Saturday.AddDays(7 * 52));

        // Assert
        runs.Should().BeTrue();
    }

    [Fact]
    public void Should_Occupy_The_Court_When_The_Hours_Overlap()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().WithHours(new(18, 0), new(21, 0)).Build();

        // Act
        var overlaps = openPlay.Occupies(Saturday, new(20, 0), new(21, 0));

        // Assert
        overlaps.Should().BeTrue();
    }

    [Fact]
    public void Should_Not_Occupy_The_Court_When_The_Hours_Only_Touch()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().WithHours(new(18, 0), new(21, 0)).Build();

        // Act
        var before = openPlay.Occupies(Saturday, new(17, 0), new(18, 0));
        var after = openPlay.Occupies(Saturday, new(21, 0), new(22, 0));

        // Assert
        using (new AssertionScope())
        {
            before.Should().BeFalse();
            after.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Close_Registration_When_The_Cutoff_Is_Reached()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder()
            .WithHours(new(18, 0), new(21, 0))
            .WithCutoffMinutes(60)
            .Build();

        // Act
        var justBefore = openPlay.IsOpenForRegistration(Saturday, Saturday.ToDateTime(new(16, 59)));
        var atCutoff = openPlay.IsOpenForRegistration(Saturday, Saturday.ToDateTime(new(17, 0)));

        // Assert
        using (new AssertionScope())
        {
            justBefore.Should().BeTrue();
            atCutoff.Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Not_Open_Registration_When_The_Date_Is_Not_A_Session()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();

        // Act
        var open = openPlay.IsOpenForRegistration(Sunday, Saturday.ToDateTime(new(8, 0)));

        // Assert
        open.Should().BeFalse();
    }

    [Fact]
    public void Should_Add_The_Platform_Fee_Without_Discount_When_There_Is_No_Early_Bird()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().WithRegistrationFee(150m).Build();

        // Act
        var price = openPlay.PriceFor(Saturday, Saturday.ToDateTime(new(8, 0)), 15m);

        // Assert
        using (new AssertionScope())
        {
            price.Discount.Should().Be(0m);
            price.PlatformFee.Should().Be(15m);
            price.Total.Should().Be(165m);
        }
    }

    [Fact]
    public void Should_Take_The_Fixed_Discount_Off_The_Fee_Only_When_Registered_Before_The_Early_Bird_Deadline()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder()
            .WithRegistrationFee(150m)
            .WithEarlyBird(OpenPlayDiscountKind.Fixed, 50m, (int)TimeSpan.FromDays(3).TotalMinutes)
            .Build();
        var fourDaysBefore = Saturday.AddDays(-4).ToDateTime(new(18, 0));

        // Act
        var price = openPlay.PriceFor(Saturday, fourDaysBefore, 15m);

        // Assert
        using (new AssertionScope())
        {
            price.Discount.Should().Be(50m);
            price.Total.Should().Be(115m);
        }
    }

    [Fact]
    public void Should_Charge_The_Full_Fee_When_Registered_After_The_Early_Bird_Deadline()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder()
            .WithRegistrationFee(150m)
            .WithEarlyBird(OpenPlayDiscountKind.Fixed, 50m, (int)TimeSpan.FromDays(3).TotalMinutes)
            .Build();
        var exactlyThreeDaysBefore = Saturday.AddDays(-3).ToDateTime(new(18, 0));

        // Act
        var price = openPlay.PriceFor(Saturday, exactlyThreeDaysBefore, 15m);

        // Assert
        price.Discount.Should().Be(0m);
    }

    [Fact]
    public void Should_Take_A_Percentage_Of_The_Fee_When_The_Early_Bird_Is_A_Percentage()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder()
            .WithRegistrationFee(150m)
            .WithEarlyBird(OpenPlayDiscountKind.Percentage, 10m, (int)TimeSpan.FromDays(3).TotalMinutes)
            .Build();
        var aWeekBefore = Saturday.AddDays(-7).ToDateTime(new(18, 0));

        // Act
        var price = openPlay.PriceFor(Saturday, aWeekBefore, 15m);

        // Assert
        using (new AssertionScope())
        {
            price.Discount.Should().Be(15m);
            price.Total.Should().Be(150m);
        }
    }

    [Fact]
    public void Should_Reject_A_Fixed_Discount_When_It_Is_More_Than_The_Fee()
    {
        // Arrange
        var builder = new OpenPlayBuilder()
            .WithRegistrationFee(100m)
            .WithEarlyBird(OpenPlayDiscountKind.Fixed, 150m, 4320);

        // Act
        var build = () => builder.Build();

        // Assert
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_An_Early_Bird_When_Its_Deadline_Is_After_Registration_Closes()
    {
        // Arrange
        var builder = new OpenPlayBuilder()
            .WithCutoffMinutes(120)
            .WithEarlyBird(OpenPlayDiscountKind.Fixed, 20m, 60);

        // Act
        var build = () => builder.Build();

        // Assert
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_A_Percentage_When_It_Is_Over_One_Hundred()
    {
        // Arrange
        // Act
        var create = () => new OpenPlayEarlyBird(OpenPlayDiscountKind.Percentage, 101m, 60);

        // Assert
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Should_Reject_The_Open_Play_When_No_Day_Is_Checked()
    {
        // Arrange
        var builder = new OpenPlayBuilder().OnDays(OpenPlayDays.None);

        // Act
        var build = () => builder.Build();

        // Assert
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Reject_The_Open_Play_When_It_Ends_Before_It_Starts()
    {
        // Arrange
        var builder = new OpenPlayBuilder().WithHours(new(21, 0), new(18, 0));

        // Act
        var build = () => builder.Build();

        // Assert
        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_Release_Every_Later_Date_When_The_Open_Play_Is_Ended()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Running(Saturday, null).Build();

        // Act
        openPlay.End(Saturday.AddDays(7), TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            openPlay.RunsOn(Saturday.AddDays(7)).Should().BeTrue();
            openPlay.RunsOn(Saturday.AddDays(14)).Should().BeFalse();
            openPlay.EndedAt.Should().Be(TestTimes.UtcNow);
        }
    }

    [Fact]
    public void Should_Keep_The_Earlier_End_Date_When_Ended_After_It()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Running(Saturday, Saturday.AddDays(7)).Build();

        // Act
        openPlay.End(Saturday.AddDays(28), TestTimes.UtcNow);

        // Assert
        openPlay.EndDate.Should().Be(Saturday.AddDays(7));
    }

    [Fact]
    public void Should_Share_Hours_When_A_Weekday_And_The_Hours_Overlap()
    {
        // Arrange
        var saturdays = new OpenPlayBuilder()
            .OnDays(OpenPlayDays.Saturday)
            .WithHours(new(18, 0), new(21, 0))
            .Build();
        var weekends = new OpenPlayBuilder()
            .OnDays(OpenPlayDays.Saturday | OpenPlayDays.Sunday)
            .WithHours(new(20, 0), new(22, 0))
            .Build();

        // Act
        var shares = saturdays.SharesHoursWith(weekends);

        // Assert
        shares.Should().BeTrue();
    }

    [Fact]
    public void Should_Not_Share_Hours_When_No_Weekday_Is_In_Common()
    {
        // Arrange
        var saturdays = new OpenPlayBuilder().OnDays(OpenPlayDays.Saturday).Build();
        var mondays = new OpenPlayBuilder().OnDays(OpenPlayDays.Monday).Build();

        // Act
        var shares = saturdays.SharesHoursWith(mondays);

        // Assert
        shares.Should().BeFalse();
    }

    [Fact]
    public void Should_Not_Share_Hours_When_One_Ends_Before_The_Other_Starts()
    {
        // Arrange
        var january = new OpenPlayBuilder().Running(Saturday, Saturday.AddDays(7)).Build();
        var later = new OpenPlayBuilder().Running(Saturday.AddDays(14), null).Build();

        // Act
        var shares = january.SharesHoursWith(later);

        // Assert
        shares.Should().BeFalse();
    }

    [Fact]
    public void Should_Not_Share_Hours_When_The_Hours_Only_Touch()
    {
        // Arrange
        var evening = new OpenPlayBuilder().WithHours(new(18, 0), new(21, 0)).Build();
        var late = new OpenPlayBuilder().WithHours(new(21, 0), new(22, 0)).Build();

        // Act
        var shares = evening.SharesHoursWith(late);

        // Assert
        shares.Should().BeFalse();
    }

    [Fact]
    public void Should_Start_As_A_Draft_That_Holds_Nothing_When_Created()
    {
        // Arrange
        // Act
        var openPlay = new OpenPlayBuilder().Build();

        // Assert
        using (new AssertionScope())
        {
            openPlay.Status.Should().Be(OpenPlayStatus.Draft);
            openPlay.BlocksCourtOn(Saturday).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Hold_The_Court_When_Published()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();

        // Act
        openPlay.Publish(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            openPlay.Status.Should().Be(OpenPlayStatus.Published);
            openPlay.PublishedAt.Should().Be(TestTimes.UtcNow);
            openPlay.BlocksCourtOn(Saturday).Should().BeTrue();
            openPlay.BlocksCourtOn(Sunday).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Refuse_Changes_When_The_Open_Play_Is_Published()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        openPlay.Publish(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act
        var update = () => openPlay.Update(
            "New title", OpenPlayLevel.Beginner, 8, 100m, new(18, 0), new(20, 0),
            OpenPlayDays.Saturday, Saturday, null, 60, null, TestTimes.UtcNow);
        var move = () => openPlay.MoveTo(TestIds.For("bookable-court", 2), TestIds.CourtId, TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            update.Should().Throw<InvalidOperationException>();
            move.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void Should_Release_The_Court_When_Taken_Back_To_A_Draft()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        openPlay.Publish(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act
        openPlay.Unpublish(TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            openPlay.Status.Should().Be(OpenPlayStatus.Draft);
            openPlay.PublishedAt.Should().BeNull();
            openPlay.BlocksCourtOn(Saturday).Should().BeFalse();
        }
    }

    [Fact]
    public void Should_Change_The_Cover_Photo_When_The_Open_Play_Is_Published()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        openPlay.Publish(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Act
        openPlay.SetCoverPhoto("icyplay/open-plays/cover", "https://res.cloudinary.com/x/cover.jpg", TestTimes.UtcNow);

        // Assert
        using (new AssertionScope())
        {
            openPlay.CoverPhotoPublicId.Should().Be("icyplay/open-plays/cover");
            openPlay.CoverPhotoUrl.Should().Be("https://res.cloudinary.com/x/cover.jpg");
        }
    }

    [Fact]
    public void Should_Refuse_A_Cover_Photo_When_The_Open_Play_Has_Ended()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        openPlay.End(Saturday, TestTimes.UtcNow);

        // Act
        var set = () => openPlay.SetCoverPhoto("icyplay/open-plays/cover", "https://res.cloudinary.com/x/c.jpg", TestTimes.UtcNow);

        // Assert
        set.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Refuse_To_Publish_When_The_Open_Play_Has_Ended()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        openPlay.End(Saturday, TestTimes.UtcNow);

        // Act
        var publish = () => openPlay.Publish(TestIds.FacilityOwnerUserId, TestTimes.UtcNow);

        // Assert
        publish.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Should_Refuse_Changes_When_The_Open_Play_Has_Ended()
    {
        // Arrange
        var openPlay = new OpenPlayBuilder().Build();
        openPlay.End(Saturday, TestTimes.UtcNow);

        // Act
        var update = () => openPlay.Update(
            "New title", OpenPlayLevel.Beginner, 8, 100m, new(18, 0), new(20, 0),
            OpenPlayDays.Saturday, Saturday, null, 60, null, TestTimes.UtcNow);

        // Assert
        update.Should().Throw<InvalidOperationException>();
    }
}
