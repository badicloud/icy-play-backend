namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// The weekdays an open play runs on: the checkboxes on the form. Every day
/// means all seven checked, and once a week means one checked.
/// </summary>
[Flags]
public enum OpenPlayDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    Everyday = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}

public static class OpenPlayDaysExtensions
{
    public static OpenPlayDays From(DayOfWeek day) => (OpenPlayDays)(1 << (int)day);

    public static bool Includes(this OpenPlayDays days, DayOfWeek day) => (days & From(day)) != 0;
}
