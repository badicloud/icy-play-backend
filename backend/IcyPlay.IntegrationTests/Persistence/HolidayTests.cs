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

    /// <summary>
    /// The fixed Philippine holidays seeded in AppDbContext. Spelled out rather
    /// than derived, so that renaming one in the seed fails here rather than
    /// quietly narrowing what this test checks.
    /// </summary>
    private static readonly string[] Seeded =
    [
        "New Year's Day",
        "Araw ng Kagitingan",
        "Labor Day",
        "Independence Day",
        "Ninoy Aquino Day",
        "All Saints' Day",
        "Bonifacio Day",
        "Christmas Day",
        "Rizal Day"
    ];

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
            // Of the seeded ones, not of the table: anything an admin adds — or
            // an import adds — is free to be a moving holiday, and this is a
            // shared database.
            holidays
                .Where(holiday => Seeded.Contains(holiday.Name, StringComparer.Ordinal))
                .Should().OnlyContain(holiday => holiday.RepeatsAnnually);
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

    [Fact]
    public async Task Template_ShouldBeAWorkbookItsOwnImporterCanRead()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act: the empty template, straight back in.
        using var handed = new MemoryStream(sut.Template());
        var imported = await sut.ImportAsync(handed, Admin(), CancellationToken.None);

        // Assert: the two example rows are read as holidays, which is the point
        // of them — a template whose own examples do not parse is one an admin
        // will fill in the same broken way.
        using (new AssertionScope())
        {
            imported.Succeeded.Should().BeTrue();
            imported.Result!.Rejected.Should().Be(0);
            imported.Result.Rows.Should().HaveCount(2);
            imported.Result.Rows.Should().Contain(row => row.Name == "Christmas Day");
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldSkipAHolidayTheCalendarAlreadyHas()
    {
        // Arrange: Christmas is seeded, so the template's own example row is
        // one the calendar already knows.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var before = (await sut.ListAsync(includeRetired: true, CancellationToken.None)).Count;

        using var first = new MemoryStream(sut.Template());
        await sut.ImportAsync(first, Admin(), CancellationToken.None);

        // Act: the same file again.
        using var again = new MemoryStream(sut.Template());
        var second = await sut.ImportAsync(again, Admin(), CancellationToken.None);

        // Assert: nothing added the second time, and the calendar is the size
        // the first import left it.
        var after = (await sut.ListAsync(includeRetired: true, CancellationToken.None)).Count;

        using (new AssertionScope())
        {
            second.Result!.Added.Should().Be(0);
            second.Result.Skipped.Should().Be(2);
            second.Result.Rows.Should()
                .OnlyContain(row => row.Outcome == HolidayImportOutcome.Skipped);
            after.Should().Be(before + 2 - SeededExamples(before));
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldSkipADayTheFileItselfRepeats()
    {
        // Arrange: one holiday written twice in the same sheet. The database
        // has never heard of it, so only the file can catch this.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var name = $"Founders Day {Guid.NewGuid():N}";

        using var sheet = Sheet(
            [name, "2026-07-04", HolidayKind.Regular, "Yes"],
            [name, "2026-07-04", HolidayKind.Regular, "Yes"]);

        // Act
        var imported = await sut.ImportAsync(sheet, Admin(), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            imported.Result!.Added.Should().Be(1);
            imported.Result.Skipped.Should().Be(1);
            (await context.Holidays.CountAsync(holiday => holiday.Name == name)).Should().Be(1);
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldRejectTheRowsItCannotReadAndKeepTheRest()
    {
        // Arrange: a good row, a nameless one, an unreadable date and a kind
        // that is not one.
        await using var context = database.CreateContext();
        var sut = CreateService(context);
        var good = $"Fiesta {Guid.NewGuid():N}";

        using var sheet = Sheet(
            [good, "2026-08-19", HolidayKind.Regular, "No"],
            ["", "2026-08-20", HolidayKind.Regular, "No"],
            ["Nameless date", "the fourth of July", HolidayKind.Regular, "No"],
            ["Wrong kind", "2026-08-21", "Bank holiday", "No"]);

        // Act
        var imported = await sut.ImportAsync(sheet, Admin(), CancellationToken.None);

        // Assert: one bad row does not cost the admin the whole file, and each
        // rejection says which row and why.
        using (new AssertionScope())
        {
            imported.Result!.Added.Should().Be(1);
            imported.Result.Rejected.Should().Be(3);
            imported.Result.Rows
                .Where(row => row.Outcome == HolidayImportOutcome.Rejected)
                .Should().OnlyContain(row => row.Reason != null && row.Row > 1);
            (await context.Holidays.AnyAsync(holiday => holiday.Name == good)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ImportAsync_ShouldRefuseAFileThatIsNotTheTemplate()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = CreateService(context);

        // Act
        using var nonsense = new MemoryStream("Name,Date;Christmas,2026-12-25"u8.ToArray());
        var imported = await sut.ImportAsync(nonsense, Admin(), CancellationToken.None);

        // Assert: a CSV renamed, a PDF, a photograph — all the same answer.
        imported.Failure.Should().Be(HolidayImportFailure.Unreadable);
    }

    /// <summary>
    /// The seeded calendar already has Christmas, so one of the template's two
    /// example rows is a skip rather than an add on a first import.
    /// </summary>
    private static int SeededExamples(int _) => 1;

    /// <summary>A workbook in the template's shape, filled with the given rows.</summary>
    private static MemoryStream Sheet(params string[][] rows)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet(HolidayTemplate.SheetName);

        for (var column = 0; column < HolidayTemplate.Columns.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = HolidayTemplate.Columns[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                sheet.Cell(row + 2, column + 1).Value = rows[row][column];
            }
        }

        var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        buffer.Position = 0;

        return buffer;
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
