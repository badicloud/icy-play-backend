using System.Text.Json;
using IcyPlay.Application.Audit;
using IcyPlay.Domain.Audit;
using IcyPlay.Infrastructure.Persistence;

namespace IcyPlay.Infrastructure.Audit;

public sealed class AuditLogger(AppDbContext db, TimeProvider timeProvider) : IAuditLogger
{
    public void RecordChange(
        AuditActor actor,
        string action,
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after,
        string? reason = null)
    {
        var changed = after.Keys
            .Where(field => !string.Equals(
                Value(before, field),
                after[field],
                StringComparison.Ordinal))
            .ToArray();

        if (changed.Length == 0)
        {
            // A save that altered nothing is not a change, and recording it
            // would bury the entries that matter.
            return;
        }

        Add(
            actor,
            action,
            entityType,
            entityId,
            Serialize(changed.ToDictionary(field => field, field => Value(before, field))),
            Serialize(changed.ToDictionary(field => field, field => after[field])),
            reason);
    }

    public void RecordEvent(
        AuditActor actor,
        string action,
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<string, string?>? details = null,
        string? reason = null) =>
        Add(actor, action, entityType, entityId, null, Serialize(details), reason);

    private void Add(
        AuditActor actor,
        string action,
        string entityType,
        Guid entityId,
        string? oldValuesJson,
        string? newValuesJson,
        string? reason) =>
        db.AuditLogs.Add(new AuditLog(
            actor.UserId,
            actor.Role,
            action,
            entityType,
            entityId,
            oldValuesJson,
            newValuesJson,
            reason,
            actor.IpAddress,
            actor.UserAgent,
            timeProvider.GetUtcNow()));

    private static string? Value(IReadOnlyDictionary<string, string?> values, string field) =>
        values.TryGetValue(field, out var value) ? value : null;

    private static string? Serialize(IReadOnlyDictionary<string, string?>? values) =>
        values is null || values.Count == 0 ? null : JsonSerializer.Serialize(values);
}
