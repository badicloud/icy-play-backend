using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// A day a court may charge its holiday rate on. A table rather than a fixed
/// list, because half the Philippine calendar moves: Maundy Thursday, Eid'l
/// Fitr and the proclaimed special days land on different dates every year, and
/// a venue should not need a deploy to charge correctly for them.
/// </summary>
public sealed class Holiday : Entity
{
    private Holiday()
    {
    }

    public Holiday(
        string name,
        DateOnly date,
        string kind,
        bool repeatsAnnually,
        DateTimeOffset createdAt)
    {
        Name = name.Trim();
        Date = date;
        Kind = kind.Trim();
        RepeatsAnnually = repeatsAnnually;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// The day itself. For a repeating holiday only the month and day are read,
    /// so the year here is simply the one it was first recorded against.
    /// </summary>
    public DateOnly Date
    {
        get; private set;
    }

    public string Kind { get; private set; } = string.Empty;

    /// <summary>
    /// True for a holiday fixed to a date, like Christmas. False for one that
    /// moves, which has to be added again for each year it falls in.
    /// </summary>
    public bool RepeatsAnnually
    {
        get; private set;
    }

    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Whether this entry covers the given day. A repeating holiday matches on
    /// month and day alone; a moving one only on the exact date it was recorded
    /// against, which is the whole reason the distinction is stored.
    /// </summary>
    public bool Covers(DateOnly day)
    {
        if (!IsActive)
        {
            return false;
        }

        return RepeatsAnnually
            ? Date.Month == day.Month && Date.Day == day.Day
            : Date == day;
    }

    public void Update(string name, DateOnly date, string kind, bool repeatsAnnually, DateTimeOffset now)
    {
        Name = name.Trim();
        Date = date;
        Kind = kind.Trim();
        RepeatsAnnually = repeatsAnnually;
        UpdatedAt = now;
    }

    /// <summary>
    /// Retired, not deleted. A booking priced as a holiday needs the day that
    /// made it one to still be there when the receipt is questioned.
    /// </summary>
    public void Retire(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}

/// <summary>
/// The two kinds Philippine law distinguishes. Kept as text rather than an enum
/// for the same reason the table exists: a proclamation can invent a label.
/// </summary>
public static class HolidayKind
{
    public const string Regular = "Regular";
    public const string SpecialNonWorking = "Special non-working";

    public static readonly IReadOnlyList<string> All = [Regular, SpecialNonWorking];

    public static bool IsSupported(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.Ordinal);
}
