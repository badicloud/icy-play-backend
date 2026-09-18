using FluentValidation;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// The one rule that says a venue's time zone is a real one.
///
/// Every hour this platform sells is a wall-clock hour at the venue: opening
/// times, peak windows, holidays and "has this hour gone" are all asked on the
/// venue's clock. Reading that clock means resolving the stored zone, and an
/// unresolvable zone does not fail — it quietly answers in UTC, which in
/// Manila is eight hours out. Nothing throws, nothing is logged, and the venue
/// simply runs a third of a day wrong until somebody notices by hand.
///
/// So the zone is checked where a person can still fix it: as they type it.
/// </summary>
public static class TimeZoneRules
{
    /// <summary>
    /// Whether the machine can turn this into an actual offset.
    ///
    /// Both spellings are accepted because both work: the IANA ids the rest of
    /// the world uses ("Asia/Manila") and the Windows ids a Windows host also
    /// carries ("Singapore Standard Time"). Rejecting either would refuse a
    /// zone the platform can read perfectly well.
    /// </summary>
    public static bool IsReal(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _);

    /// <summary>
    /// Says the venue's clock has to be one the platform can read.
    ///
    /// Named rather than repeated at each call site: there are two ways into a
    /// facility's details and a rule written twice is a rule that will one day
    /// only be half true.
    /// </summary>
    public static IRuleBuilderOptions<T, string> MustBeARealTimeZone<T>(
        this IRuleBuilder<T, string> rule) =>
        rule.Must(IsReal!)
            .WithMessage(
                "'{PropertyValue}' is not a time zone this platform can read. " +
                "Use a name like Asia/Manila.");
}
