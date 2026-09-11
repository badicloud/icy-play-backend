namespace IcyPlay.Application.Audit;

/// <summary>
/// Records a change alongside the change itself. Deliberately not async and
/// deliberately without a save: the entry is added to the same unit of work as
/// the thing it describes, so the two commit together or neither does.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Records the difference between two snapshots. Does nothing when they
    /// match: an audit trail full of "nothing changed" rows buries the entries
    /// that matter.
    /// </summary>
    void RecordChange(
        AuditActor actor,
        string action,
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<string, string?> before,
        IReadOnlyDictionary<string, string?> after,
        string? reason = null);

    /// <summary>An event with no before and after, such as a resend.</summary>
    void RecordEvent(
        AuditActor actor,
        string action,
        string entityType,
        Guid entityId,
        IReadOnlyDictionary<string, string?>? details = null,
        string? reason = null);
}

/// <summary>
/// Who made the change and from where. Built in the API layer, because the
/// address and the user agent are HTTP facts and belong nowhere else.
/// </summary>
public sealed record AuditActor(
    Guid? UserId,
    string Role,
    string? IpAddress = null,
    string? UserAgent = null);
