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
