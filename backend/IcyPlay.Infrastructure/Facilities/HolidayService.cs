using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Facilities;

public sealed class HolidayService(
    AppDbContext db,
    IAuditLogger audit,
    TimeProvider timeProvider) : IHolidayService
{
    public async Task<IReadOnlyCollection<HolidayListItem>> ListAsync(
        bool includeRetired,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var holidays = await db.Holidays
            .AsNoTracking()
            .Where(holiday => includeRetired || holiday.IsActive)
            .ToArrayAsync(ct);

        // Ordered by when each one next comes round rather than by its stored
        // date: a repeating holiday's stored year is an artefact, and sorting
        // by it would put Christmas 2026 beside a special day from 2027.
        return
        [
            .. holidays
                .Select(holiday => new HolidayListItem(
                    holiday.Id,
                    holiday.Name,
                    holiday.Date,
                    holiday.Kind,
                    holiday.RepeatsAnnually,
                    holiday.IsActive,
                    NextOccurrence(holiday, today)))
                .OrderBy(item => item.NextOccurrence ?? DateOnly.MaxValue)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
        ];
    }

    public byte[] Template() => HolidayWorkbook.Write();

    public async Task<HolidayImportOutput> ImportAsync(
        Stream workbook,
        AuditActor actor,
        CancellationToken ct)
    {
        var (failure, parsed) = HolidayWorkbook.Read(workbook);

        if (failure != HolidayImportFailure.None)
        {
            return HolidayImportOutput.Fail(failure);
        }

        // The whole calendar, read once. An import of twenty rows would
        // otherwise ask the database twenty times whether it already knew each
        // one, and the calendar is small enough to hold.
        var existing = await db.Holidays
            .AsNoTracking()
            .Select(holiday => new { holiday.Name, holiday.Date })
            .ToArrayAsync(ct);

        var known = new HashSet<(string Name, DateOnly Date)>(
            existing.Select(holiday => (holiday.Name, holiday.Date)));

        var now = timeProvider.GetUtcNow();
        var rows = new List<HolidayImportRow>();
        var added = new List<Holiday>();

        foreach (var row in parsed)
        {
            if (!row.IsUsable)
            {
                rows.Add(new HolidayImportRow(
                    row.Row,
                    row.Name,
                    row.Date,
                    HolidayImportOutcome.Rejected,
                    row.Problem));
                continue;
            }

            var key = (row.Name!, row.Date!.Value);

            // The same set answers both duplicate questions: one against the
            // calendar, and one against the rows already taken from this file.
            // A sheet listing Christmas twice is a duplicate the database has
            // not heard of yet.
            if (!known.Add(key))
            {
                rows.Add(new HolidayImportRow(
                    row.Row,
                    row.Name,
                    row.Date,
                    HolidayImportOutcome.Skipped,
                    "Already on the calendar."));
                continue;
            }

            added.Add(new Holiday(row.Name!, row.Date.Value, row.Kind!, row.RepeatsAnnually, now));
            rows.Add(new HolidayImportRow(
                row.Row,
                row.Name,
                row.Date,
                HolidayImportOutcome.Added,
                null));
        }

        if (added.Count > 0)
        {
            db.Holidays.AddRange(added);

            // One audit line for the file rather than one per holiday: what was
            // done here was an import, and the rows are in its record.
            audit.RecordEvent(
                actor,
                AuditAction.HolidaysImported,
                AuditEntityType.Holiday,
                added[0].Id,
                new Dictionary<string, string?>
                {
                    ["added"] = added.Count.ToString(CultureInfo.InvariantCulture),
                    ["skipped"] = rows
                        .Count(item => item.Outcome == HolidayImportOutcome.Skipped)
                        .ToString(CultureInfo.InvariantCulture),
                    ["rejected"] = rows
                        .Count(item => item.Outcome == HolidayImportOutcome.Rejected)
                        .ToString(CultureInfo.InvariantCulture),
                    ["holidays"] = string.Join(", ", added.Select(holiday => holiday.Name))
                });

            await db.SaveChangesAsync(ct);
        }

        return HolidayImportOutput.Success(new HolidayImportResult(
            added.Count,
            rows.Count(item => item.Outcome == HolidayImportOutcome.Skipped),
            rows.Count(item => item.Outcome == HolidayImportOutcome.Rejected),
            rows));
    }

    public async Task<CourtResult<Guid>> CreateAsync(
        CreateHolidayRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (await db.Holidays.AnyAsync(
                holiday => holiday.Name == name && holiday.Date == request.Date,
                ct))
        {
            return CourtResult<Guid>.Fail(CourtFailure.DuplicateHoliday);
        }

        var now = timeProvider.GetUtcNow();
        var holiday = new Holiday(name, request.Date, request.Kind, request.RepeatsAnnually, now);
        db.Holidays.Add(holiday);

        audit.RecordEvent(
            actor,
            AuditAction.HolidayCreated,
            AuditEntityType.Holiday,
            holiday.Id,
            Snapshot(holiday));

        await db.SaveChangesAsync(ct);
        return CourtResult<Guid>.Success(holiday.Id);
    }

    public async Task<CourtResult<bool>> UpdateAsync(
        Guid id,
        UpdateHolidayRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        if (holiday is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.HolidayNotFound);
        }

        var name = request.Name.Trim();

        if (await db.Holidays.AnyAsync(
                candidate =>
                    candidate.Id != id &&
                    candidate.Name == name &&
                    candidate.Date == request.Date,
                ct))
        {
            return CourtResult<bool>.Fail(CourtFailure.DuplicateHoliday);
        }

        var now = timeProvider.GetUtcNow();
        var before = Snapshot(holiday);
        holiday.Update(name, request.Date, request.Kind, request.RepeatsAnnually, now);

        audit.RecordChange(
            actor,
            AuditAction.HolidayUpdated,
            AuditEntityType.Holiday,
            holiday.Id,
            before,
            Snapshot(holiday),
            null);

        await db.SaveChangesAsync(ct);
        return CourtResult<bool>.Success(true);
    }

    public async Task<CourtResult<bool>> SetActiveAsync(
        Guid id,
        bool isActive,
        AuditActor actor,
        CancellationToken ct)
    {
        var holiday = await db.Holidays.SingleOrDefaultAsync(candidate => candidate.Id == id, ct);

        if (holiday is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.HolidayNotFound);
        }

        if (holiday.IsActive == isActive)
        {
            return CourtResult<bool>.Success(true);
        }

        var now = timeProvider.GetUtcNow();

        if (isActive)
        {
            holiday.Reinstate(now);
        }
        else
        {
            holiday.Retire(now);
        }

        audit.RecordEvent(
            actor,
            isActive ? AuditAction.HolidayReinstated : AuditAction.HolidayRetired,
            AuditEntityType.Holiday,
            holiday.Id,
            new Dictionary<string, string?> { ["name"] = holiday.Name });

        await db.SaveChangesAsync(ct);
        return CourtResult<bool>.Success(true);
    }

    public async Task<bool> IsHolidayAsync(DateOnly day, CancellationToken ct)
    {
        // The repeating ones cannot be matched on their stored date in SQL, so
        // the active rows are read and asked. A calendar's worth of rows is
        // small enough that this costs less than the index would.
        var holidays = await db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.IsActive)
            .ToArrayAsync(ct);

        return holidays.Any(holiday => holiday.Covers(day));
    }

    /// <summary>
    /// When this holiday next falls, counting today. A moving holiday that has
    /// already gone has no next date, and saying so is how the console shows
    /// which entries need adding again for the coming year.
    /// </summary>
    private static DateOnly? NextOccurrence(Holiday holiday, DateOnly today)
    {
        if (!holiday.RepeatsAnnually)
        {
            return holiday.Date < today ? null : holiday.Date;
        }

        // 29 February in a year that has no 29 February moves to the 28th,
        // which is what the proclamations do with it in practice.
        var thisYear = SafeDate(today.Year, holiday.Date.Month, holiday.Date.Day);
        return thisYear >= today ? thisYear : SafeDate(today.Year + 1, holiday.Date.Month, holiday.Date.Day);
    }

    private static DateOnly SafeDate(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    private static Dictionary<string, string?> Snapshot(Holiday holiday) =>
        new()
        {
            ["name"] = holiday.Name,
            ["date"] = holiday.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["kind"] = holiday.Kind,
            ["repeatsAnnually"] = holiday.RepeatsAnnually.ToString()
        };
}
