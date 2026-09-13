using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One bookable surface inside a facility. Every booking will belong to a
/// court, which is why the rules for how it is booked live here rather than on
/// the facility: a badminton court and a basketball court in the same building
/// keep different slot lengths and different minimums.
/// </summary>
public sealed class Court : Entity
{
    private Court()
    {
    }

    public Court(
        Guid facilityId,
        Guid facilityOwnerId,
        string name,
        int displayOrder,
        string? description,
        CourtSpace space,
        CourtBookingRules bookingRules,
        DateTimeOffset createdAt)
    {
        FacilityId = facilityId;
        FacilityOwnerId = facilityOwnerId;
        Name = name.Trim();
        DisplayOrder = displayOrder;
        Description = Clean(description);
        ApplySpace(space);
        ApplyBookingRules(bookingRules);
        CreatedAt = createdAt;
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;

    /// <summary>
    /// Denormalised from the facility so an ownership check is one predicate
    /// rather than a join on every query that touches a court.
    /// </summary>
    public Guid FacilityOwnerId
    {
        get; private set;
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Sorted on, because alphabetical puts "Court 10" before "Court 2".
    /// </summary>
    public int DisplayOrder
    {
        get; private set;
    }
    public string? Description
    {
        get; private set;
    }

    public string VenueType { get; private set; } = CourtVenueType.Covered;
    public string? Surface
    {
        get; private set;
    }

    /// <summary>Decides whether an outdoor court can be booked after dark.</summary>
    public bool HasLighting
    {
        get; private set;
    }
    public string? SizeLabel
    {
        get; private set;
    }
    public int? Capacity
    {
        get; private set;
    }

    /// <summary>What is at the court itself: a net, a scoreboard. Parking and showers belong to the facility.</summary>
    public string? Equipment
    {
        get; private set;
    }

    /// <summary>The smallest block the calendar is divided into.</summary>
    public int SlotLengthMinutes { get; private set; } = 60;

    /// <summary>Never shorter than one slot, and always a whole number of them.</summary>
    public int MinimumDurationMinutes { get; private set; } = 60;

    /// <summary>Changeover time held between one booking and the next.</summary>
    public int BufferMinutes
    {
        get; private set;
    }

    /// <summary>
    /// True by default, so an owner with eight courts types the schedule once.
    /// The outdoor court that closes early sets its own.
    /// </summary>
    public bool UsesFacilityHours { get; private set; } = true;

    /// <summary>Permanent. Maintenance is the temporary one.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// When the peak rate applies. On the court rather than on each sport,
    /// because a venue is busy at the same hours whatever is being played on
    /// it. Null until someone sets a peak rate worth having a window for.
    /// </summary>
    public TimeOnly? PeakStartsAt
    {
        get; private set;
    }
    public TimeOnly? PeakEndsAt
    {
        get; private set;
    }

    /// <summary>
    /// Which days the window counts on. Both false means the window is set but
    /// applies nowhere, which the service refuses rather than storing.
    /// </summary>
    public bool PeakOnWeekdays
    {
        get; private set;
    }
    public bool PeakOnWeekends
    {
        get; private set;
    }

    public bool HasPeakWindow => PeakStartsAt is not null && PeakEndsAt is not null;

    public ICollection<CourtSport> Sports { get; private set; } = [];
    public ICollection<CourtOperatingHour> OperatingHours { get; private set; } = [];

    public void UpdateDetails(
        string name,
        int displayOrder,
        string? description,
        CourtSpace space,
        CourtBookingRules bookingRules,
        DateTimeOffset now)
    {
        Name = name.Trim();
        DisplayOrder = displayOrder;
        Description = Clean(description);
        ApplySpace(space);
        ApplyBookingRules(bookingRules);
        UpdatedAt = now;
    }

    public void SetPeakWindow(
        TimeOnly? startsAt,
        TimeOnly? endsAt,
        bool onWeekdays,
        bool onWeekends,
        DateTimeOffset now)
    {
        PeakStartsAt = startsAt;
        PeakEndsAt = endsAt;
        PeakOnWeekdays = onWeekdays;
        PeakOnWeekends = onWeekends;
        UpdatedAt = now;
    }

    /// <summary>
    /// Whether a moment falls inside the peak window. This is what decides
    /// between a court's standard and peak rate. The window is held inside the
    /// court's opening hours, which do not run past midnight, so neither does
    /// this.
    /// </summary>
    public bool IsPeakAt(DayOfWeek day, TimeOnly time)
    {
        if (PeakStartsAt is not TimeOnly start || PeakEndsAt is not TimeOnly end)
        {
            return false;
        }

        var isWeekend = day is DayOfWeek.Saturday or DayOfWeek.Sunday;

        if (isWeekend ? !PeakOnWeekends : !PeakOnWeekdays)
        {
            return false;
        }

        return time >= start && time < end;
    }

    public static bool IsWeekend(DayOfWeek day) => day is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>
    /// What one division is called: the court, the sport it is set up for, and
    /// which of them it is. Derived rather than stored, so renaming the court
    /// renames its divisions with it instead of leaving a name that lies.
    ///
    /// A court played whole keeps its own name; numbering one of one would
    /// only invite the question of where the second is.
    /// </summary>
    public static string DivisionName(string courtName, string sportName, int number, int divisions) =>
        divisions <= 1
            ? courtName
            : $"{courtName} · {sportName} {number}";

    public void SetUsesFacilityHours(bool usesFacilityHours, DateTimeOffset now)
    {
        UsesFacilityHours = usesFacilityHours;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reactivate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    private void ApplySpace(CourtSpace space)
    {
        VenueType = CourtVenueType.IsSupported(space.VenueType)
            ? space.VenueType
            : throw new ArgumentException("Unknown venue type.", nameof(space));
        Surface = Clean(space.Surface);
        HasLighting = space.HasLighting;
        SizeLabel = Clean(space.SizeLabel);
        Capacity = space.Capacity is > 0 ? space.Capacity : null;
        Equipment = Clean(space.Equipment);
    }

    private void ApplyBookingRules(CourtBookingRules rules)
    {
        if (rules.SlotLengthMinutes <= 0)
        {
            throw new ArgumentException("A slot has to be longer than nothing.", nameof(rules));
        }

        // A minimum that is not a whole number of slots cannot be booked: the
        // calendar only offers slot boundaries.
        if (rules.MinimumDurationMinutes < rules.SlotLengthMinutes ||
            rules.MinimumDurationMinutes % rules.SlotLengthMinutes != 0)
        {
            throw new ArgumentException(
                "The minimum booking must be a whole number of slots, and at least one.",
                nameof(rules));
        }

        if (rules.BufferMinutes < 0)
        {
            throw new ArgumentException("A buffer cannot be negative.", nameof(rules));
        }

        SlotLengthMinutes = rules.SlotLengthMinutes;
        MinimumDurationMinutes = rules.MinimumDurationMinutes;
        BufferMinutes = rules.BufferMinutes;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CourtSpace(
    string VenueType,
    string? Surface,
    bool HasLighting,
    string? SizeLabel,
    int? Capacity,
    string? Equipment);

public sealed record CourtBookingRules(
    int SlotLengthMinutes,
    int MinimumDurationMinutes,
    int BufferMinutes);

/// <summary>
/// Three, not two. A covered court has a roof and open sides, which is neither
/// indoors nor out in the rain, and it is the commonest kind here.
/// </summary>
public static class CourtVenueType
{
    public const string Indoor = "Indoor";
    public const string Covered = "Covered";
    public const string Outdoor = "Outdoor";

    public static readonly IReadOnlyCollection<string> All = [Indoor, Covered, Outdoor];

    public static bool IsSupported(string value) => All.Contains(value);
}

public static class CourtSurface
{
    public const string Wood = "Wood";
    public const string Concrete = "Concrete";
    public const string Synthetic = "Synthetic";
    public const string Acrylic = "Acrylic";
    public const string Sand = "Sand";
    public const string Grass = "Grass";

    public static readonly IReadOnlyCollection<string> All =
        [Wood, Concrete, Synthetic, Acrylic, Sand, Grass];

    public static bool IsSupported(string value) => All.Contains(value);
}
