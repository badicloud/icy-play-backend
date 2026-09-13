using FluentAssertions.Execution;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Audit;
using IcyPlay.Infrastructure.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class HolidayTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_ShouldSeedTheFixedPhilippineHolidays()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act
        var holidays = await sut.ListAsync(includeRetired: false, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            holidays.Should().Contain(holiday => holiday.Name == "Christmas Day");
            holidays.Should().Contain(holiday => holiday.Name == "Araw ng Kagitingan");
            // The movable ones are deliberately absent: they land on a
            // different date each year, which is why the table is managed.
            holidays.Should().NotContain(holiday => holiday.Name == "Good Friday");
            holidays.Should().OnlyContain(holiday => holiday.RepeatsAnnually);
        }
    }

    [Fact]
    public async Task IsHolidayAsync_ShouldMatchARepeatingHolidayInAnyYear()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act: the row is stored against 2026, but Christmas is Christmas.
        var christmas2030 = await sut.IsHolidayAsync(new DateOnly(2030, 12, 25), CancellationToken.None);
        var boxingDay = await sut.IsHolidayAsync(new DateOnly(2030, 12, 26), CancellationToken.None);

        using (new AssertionScope())
        {
            christmas2030.Should().BeTrue();
            boxingDay.Should().BeFalse();
        }
    }

    [Fact]
    public async Task IsHolidayAsync_ShouldMatchAMovingHolidayOnlyInItsOwnYear()
    {
        // Arrange: Good Friday, which is a different date every year.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        await sut.CreateAsync(
            new CreateHolidayRequest(
                $"Good Friday {Guid.NewGuid():N}",
                new DateOnly(2026, 4, 3),
                HolidayKind.Regular,
                RepeatsAnnually: false),
            Admin(),
            CancellationToken.None);

        // Act
        var thatYear = await sut.IsHolidayAsync(new DateOnly(2026, 4, 3), CancellationToken.None);
        var nextYear = await sut.IsHolidayAsync(new DateOnly(2027, 4, 3), CancellationToken.None);

        // Assert: repeating it would put Good Friday on the wrong day for every
        // year after the one it was recorded for.
        using (new AssertionScope())
        {
            thatYear.Should().BeTrue();
            nextYear.Should().BeFalse();
        }
    }

    [Fact]
    public async Task IsHolidayAsync_ShouldIgnoreARetiredHoliday()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var created = await sut.CreateAsync(
            new CreateHolidayRequest(
                $"Local fiesta {Guid.NewGuid():N}",
                new DateOnly(2026, 7, 14),
                HolidayKind.SpecialNonWorking,
                RepeatsAnnually: true),
            Admin(),
            CancellationToken.None);

        // Act
        await sut.SetActiveAsync(created.Value, isActive: false, Admin(), CancellationToken.None);

        // Assert: retired, not deleted -- a booking already priced as a holiday
        // still needs the day that made it one.
        using (new AssertionScope())
        {
            (await sut.IsHolidayAsync(new DateOnly(2026, 7, 14), CancellationToken.None))
                .Should().BeFalse();
            (await context.Holidays.AnyAsync(holiday => holiday.Id == created.Value))
                .Should().BeTrue();
        }
    }

    [Fact]
    public async Task CreateAsync_ShouldRefuseTheSameHolidayOnTheSameDateTwice()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var name = $"Founders Day {Guid.NewGuid():N}";
        var request = new CreateHolidayRequest(
            name,
            new DateOnly(2026, 3, 15),
            HolidayKind.SpecialNonWorking,
            RepeatsAnnually: true);

        await sut.CreateAsync(request, Admin(), CancellationToken.None);

        // Act
        var result = await sut.CreateAsync(request, Admin(), CancellationToken.None);

        // Assert
        result.Failure.Should().Be(CourtFailure.DuplicateHoliday);
    }

    [Fact]
    public async Task ListAsync_ShouldOrderByWhenEachOneNextComesRound()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act
        var holidays = await sut.ListAsync(includeRetired: false, CancellationToken.None);
        var dated = holidays.Where(holiday => holiday.NextOccurrence is not null).ToArray();

        // Assert: a repeating holiday's stored year is an artefact, so sorting
        // by it would put next Christmas beside a date already gone.
        using (new AssertionScope())
        {
            dated.Should().NotBeEmpty();
            dated.Select(holiday => holiday.NextOccurrence).Should().BeInAscendingOrder();
            dated.Should().OnlyContain(holiday =>
                holiday.NextOccurrence >= DateOnly.FromDateTime(Now.UtcDateTime));
        }
    }

    private static HolidayService CreateService(AppDbContext context) => new(
        context,
        new AuditLogger(context, new FixedTimeProvider(Now)),
        new FixedTimeProvider(Now));

    private static AuditActor Admin() => new(Guid.NewGuid(), UserRoleName.PlatformAdmin);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
