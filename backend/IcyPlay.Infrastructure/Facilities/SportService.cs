using IcyPlay.Application.Audit;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Facilities;

public sealed class SportService(
    AppDbContext db,
    IAuditLogger audit,
    TimeProvider timeProvider) : ISportService
{
    public async Task<IReadOnlyCollection<SportListItem>> ListAsync(
        bool includeRetired,
        CancellationToken ct) =>
        await db.Sports
            .AsNoTracking()
            .Where(sport => includeRetired || sport.IsActive)
            .OrderBy(sport => sport.Category)
            .ThenBy(sport => sport.DisplayOrder)
            .ThenBy(sport => sport.Name)
            .Select(sport => new SportListItem(
                sport.Id,
                sport.Key,
                sport.Name,
                sport.Category,
                sport.DisplayOrder,
                sport.IsActive,
                // Retiring one is a decision with a number attached, so the
                // console can say how many courts it would affect.
                db.CourtSports.Count(link => link.SportId == sport.Id)))
            .ToArrayAsync(ct);

    public async Task<CourtResult<Guid>> CreateAsync(
        CreateSportRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        // Derived once from the name and stable afterwards, like a slug: the
        // customer-facing filter is built from it.
        var key = Facility.ToSlug(request.Name);

        if (key.Length == 0)
        {
            return CourtResult<Guid>.Fail(CourtFailure.DuplicateSportKey);
        }

        if (await db.Sports.AnyAsync(sport => sport.Key == key, ct))
        {
            return CourtResult<Guid>.Fail(CourtFailure.DuplicateSportKey);
        }

        var now = timeProvider.GetUtcNow();
        var sport = new Sport(key, request.Name, request.Category, request.DisplayOrder, now);
        db.Sports.Add(sport);

        audit.RecordEvent(
            actor,
            AuditAction.SportCreated,
            AuditEntityType.Sport,
            sport.Id,
            new Dictionary<string, string?>
            {
                ["key"] = sport.Key,
                ["name"] = sport.Name,
                ["category"] = sport.Category
            });

        await db.SaveChangesAsync(ct);
        return CourtResult<Guid>.Success(sport.Id);
    }

    public async Task<CourtResult<bool>> UpdateAsync(
        Guid id,
        UpdateSportRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        var sport = await db.Sports.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);

        if (sport is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.UnknownSport);
        }

        var before = Snapshot(sport);

        // The key is deliberately left alone. Renaming "Table tennis" to "Ping
        // pong" should not move the address customers filter on.
        sport.Rename(request.Name, request.Category, request.DisplayOrder, timeProvider.GetUtcNow());

        audit.RecordChange(
            actor,
            AuditAction.SportUpdated,
            AuditEntityType.Sport,
            sport.Id,
            before,
            Snapshot(sport));

        await db.SaveChangesAsync(ct);
        return CourtResult<bool>.Success(true);
    }

    public async Task<CourtResult<bool>> SetActiveAsync(
        Guid id,
        bool isActive,
        AuditActor actor,
        CancellationToken ct)
    {
        var sport = await db.Sports.FirstOrDefaultAsync(candidate => candidate.Id == id, ct);

        if (sport is null)
        {
            return CourtResult<bool>.Fail(CourtFailure.UnknownSport);
        }

        var now = timeProvider.GetUtcNow();

        if (isActive)
        {
            sport.Reinstate(now);
        }
        else
        {
            sport.Retire(now);
        }

        audit.RecordEvent(
            actor,
            isActive ? AuditAction.SportReinstated : AuditAction.SportRetired,
            AuditEntityType.Sport,
            sport.Id,
            new Dictionary<string, string?> { ["name"] = sport.Name });

        await db.SaveChangesAsync(ct);
        return CourtResult<bool>.Success(true);
    }

    private static Dictionary<string, string?> Snapshot(Sport sport) =>
        new()
        {
            ["name"] = sport.Name,
            ["category"] = sport.Category,
            ["displayOrder"] = sport.DisplayOrder.ToString()
        };
}
