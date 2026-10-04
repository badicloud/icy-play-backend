using IcyPlay.Domain.Common;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// A recurring group session on one court: the same hours, on the same
/// weekdays, from a start date until an end date or until someone ends it.
/// Players join per head instead of renting the court.
///
/// A template, not a list of dates. The court is blocked for every date this
/// covers, and the block is worked out from the rule instead of from generated
/// rows. An open play with no end date would otherwise need rows forever, and
/// the week after the last generated one would sell to a regular customer.
/// A date only gets its own <see cref="OpenPlaySession"/> once something happens
/// on it: the first registration, or the desk cancelling it.
///
/// It starts as a draft. A draft is invisible to customers and blocks nothing,
/// so the desk can work on it freely. Publishing opens it for registration and
/// blocks the court. From then on it cannot be edited: players are signing up
/// for what it says.
/// </summary>
public sealed class OpenPlay : Entity
{
    private OpenPlay()
    {
    }

    public OpenPlay(
        Guid facilityId,
        Guid bookableCourtId,
        Guid courtId,
        string title,
        string level,
        int maxPlayers,
        decimal registrationFee,
        TimeOnly startsAt,
        TimeOnly endsAt,
        OpenPlayDays days,
        DateOnly startDate,
        DateOnly? endDate,
        int registrationCutoffMinutes,
        OpenPlayEarlyBird? earlyBird,
        Guid createdByUserId,
        DateTimeOffset createdAt)
    {
        FacilityId = facilityId;
        BookableCourtId = bookableCourtId;
        CourtId = courtId;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;

        Apply(title, level, maxPlayers, registrationFee, startsAt, endsAt, days,
            startDate, endDate, registrationCutoffMinutes, earlyBird);
    }

    public Guid FacilityId
    {
        get; private set;
    }
    public Facility Facility { get; private set; } = null!;

    /// <summary>The court and sport it is played on. The sport comes from here.</summary>
    public Guid BookableCourtId
    {
        get; private set;
    }
    public BookableCourt BookableCourt { get; private set; } = null!;

    /// <summary>
    /// The physical floor, copied from <see cref="BookableCourt"/> for the same
    /// reason <see cref="Bookings.BookingSlot.CourtId"/> is: availability asks
    /// what is held on this floor, and that should not need a join.
    /// </summary>
    public Guid CourtId
    {
        get; private set;
    }

    public string Title { get; private set; } = string.Empty;

    /// <summary>One of <see cref="OpenPlayLevel"/>.</summary>
    public string Level { get; private set; } = OpenPlayLevel.AllLevels;

    public int MaxPlayers
    {
        get; private set;
    }

    /// <summary>
    /// What the venue charges each player, before any early-bird discount and
    /// before the platform's top-up.
    /// </summary>
    public decimal RegistrationFee
    {
        get; private set;
    }

    /// <summary>Wall-clock time at the venue. Never past midnight, like the venue's hours.</summary>
    public TimeOnly StartsAt
    {
        get; private set;
    }

    public TimeOnly EndsAt
    {
        get; private set;
    }

    public OpenPlayDays Days
    {
        get; private set;
    }

    public DateOnly StartDate
    {
        get; private set;
    }

    /// <summary>Null means it runs until someone ends it.</summary>
    public DateOnly? EndDate
    {
        get; private set;
    }

    /// <summary>
    /// How long before a session starts registration closes. Relative, so one
    /// number works for every date of the series.
    /// </summary>
    /// <summary>
    /// How long before each session the desk can start checking players in.
    /// The window then runs until the session ends.
    /// </summary>
    public int CheckInOpensMinutes { get; private set; } = OpenPlayLimits.DefaultCheckInLeadMinutes;

    public int RegistrationCutoffMinutes
    {
        get; private set;
    }

    /// <summary>Null when there is no early-bird price.</summary>
    public OpenPlayEarlyBird? EarlyBird
    {
        get; private set;
    }

    public Guid CreatedByUserId
    {
        get; private set;
    }

    /// <summary>When someone ended the series early. <see cref="EndDate"/> says from when.</summary>
    public DateTimeOffset? EndedAt
    {
        get; private set;
    }

    /// <summary>
    /// Set only by the demonstration seeder. Removing sample data goes by this
    /// marker, never by the title, so a real open play cannot be caught by it.
    /// </summary>
    public DateTimeOffset? SeededAt
    {
        get; private set;
    }

    public bool IsSeeded => SeededAt is not null;

    /// <summary>Null while it is a draft.</summary>
    public DateTimeOffset? PublishedAt
    {
        get; private set;
    }

    public Guid? PublishedByUserId
    {
        get; private set;
    }

    public bool IsPublished => PublishedAt is not null;

    public bool HasEnded => EndedAt is not null;

    /// <summary>One of <see cref="OpenPlayStatus"/>. Derived, so it cannot disagree with the dates.</summary>
    public string Status =>
        HasEnded ? OpenPlayStatus.Ended
        : IsPublished ? OpenPlayStatus.Published
        : OpenPlayStatus.Draft;

    /// <summary>
    /// Whether customers can see it and the court is held for it. A draft
    /// holds nothing, and neither does an open play after its end date.
    /// </summary>
    public bool BlocksCourtOn(DateOnly date) => IsPublished && RunsOn(date);

    /// <summary>
    /// The cover photo's Cloudinary public id. Null when there is none, and the
    /// listing then falls back to the court's photo, then the venue's.
    /// </summary>
    public string? CoverPhotoPublicId
    {
        get; private set;
    }

    /// <summary>Cloudinary's <c>secure_url</c>, never the http one.</summary>
    public string? CoverPhotoUrl
    {
        get; private set;
    }

    public ICollection<OpenPlaySession> Sessions { get; private set; } = [];

    public int DurationMinutes => (int)(EndsAt - StartsAt).TotalMinutes;

    /// <summary>
    /// Replaces the settings. The service must check the new hours against the
    /// court's bookings first, and must refuse a change that moves a date
    /// somebody has already registered for.
    /// </summary>
    public void Update(
        string title,
        string level,
        int maxPlayers,
        decimal registrationFee,
        TimeOnly startsAt,
        TimeOnly endsAt,
        OpenPlayDays days,
        DateOnly startDate,
        DateOnly? endDate,
        int registrationCutoffMinutes,
        OpenPlayEarlyBird? earlyBird,
        DateTimeOffset now)
    {
        if (EndedAt is not null)
        {
            throw new InvalidOperationException("An open play that has ended cannot be changed.");
        }

        if (IsPublished)
        {
            throw new InvalidOperationException(
                "A published open play is open for registration and cannot be changed.");
        }

        Apply(title, level, maxPlayers, registrationFee, startsAt, endsAt, days,
            startDate, endDate, registrationCutoffMinutes, earlyBird);
        UpdatedAt = now;
    }

    /// <summary>
    /// Whether the series runs on this date. It does not look at cancelled
    /// sessions, holidays, or maintenance. Those need data this object does not
    /// have, so the availability check applies them.
    /// </summary>
    public bool RunsOn(DateOnly date) =>
        date >= StartDate
        && (EndDate is null || date <= EndDate)
        && Days.Includes(date.DayOfWeek);

    /// <summary>
    /// Whether the series holds this floor for any part of the given hours.
    /// Touching ends do not overlap, matching <see cref="Bookings.BookingSlot.Overlaps"/>.
    /// </summary>
    public bool Occupies(DateOnly date, TimeOnly startsAt, TimeOnly endsAt) =>
        RunsOn(date) && StartsAt < endsAt && startsAt < EndsAt;

    /// <summary>
    /// Whether two series would ever want the same hours on the same date: a
    /// weekday in common, date ranges that meet, and hours that overlap. It
    /// does not look at which part of the floor either is on. The caller asks
    /// <see cref="BookableCourt.ConflictsWith"/> for that.
    /// </summary>
    public bool SharesHoursWith(OpenPlay other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return (Days & other.Days) != OpenPlayDays.None
            && (EndDate is null || other.StartDate <= EndDate)
            && (other.EndDate is null || StartDate <= other.EndDate)
            && StartsAt < other.EndsAt
            && other.StartsAt < EndsAt;
    }

    /// <summary>When registration for the session on this date closes, on the venue's clock.</summary>
    /// <summary>When the desk can start checking players in for the session on this date, on the venue's clock.</summary>
    public DateTime CheckInOpensAt(DateOnly date) =>
        date.ToDateTime(StartsAt).AddMinutes(-CheckInOpensMinutes);

    /// <summary>
    /// Whether players can be checked in for this date right now: from
    /// <see cref="CheckInOpensMinutes"/> before the start until the session ends.
    /// <paramref name="venueNow"/> is the venue's wall clock, from the server.
    /// </summary>
    public bool IsCheckInOpen(DateOnly date, DateTime venueNow) =>
        RunsOn(date)
        && venueNow >= CheckInOpensAt(date)
        && venueNow < date.ToDateTime(EndsAt);

    /// <summary>
    /// Sets how long before each session check-in opens. Allowed after
    /// publishing, like the photo: it is how the venue runs its door, not part
    /// of what a player signed up for.
    /// </summary>
    public void SetCheckInWindow(int minutesBeforeStart, DateTimeOffset now)
    {
        if (HasEnded)
        {
            throw new InvalidOperationException("An open play that has ended cannot be changed.");
        }

        if (minutesBeforeStart < 0 || minutesBeforeStart > OpenPlayLimits.LongestCheckInLeadMinutes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minutesBeforeStart),
                $"Check-in can open from 0 to {OpenPlayLimits.LongestCheckInLeadMinutes / 60} hours before the start.");
        }

        CheckInOpensMinutes = minutesBeforeStart;
        UpdatedAt = now;
    }

    public DateTime RegistrationClosesAt(DateOnly date) =>
        date.ToDateTime(StartsAt).AddMinutes(-RegistrationCutoffMinutes);

    /// <summary>
    /// Whether a player can still register for this date. <paramref name="venueNow"/>
    /// is wall-clock time at the venue, from the server. The browser clock is never used.
    /// A cancelled session and a full one are the session's business, not this.
    /// </summary>
    public bool IsOpenForRegistration(DateOnly date, DateTime venueNow) =>
        RunsOn(date) && venueNow < RegistrationClosesAt(date);

    /// <summary>
    /// What one player pays for the session on this date, if they register now.
    /// The platform top-up is the owner's contract rate, added once per
    /// registration. The early-bird discount comes off the venue's fee only.
    /// </summary>
    public OpenPlayPrice PriceFor(DateOnly date, DateTime venueNow, decimal platformFee)
    {
        if (platformFee < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(platformFee), "A platform fee cannot be negative.");
        }

        var discount = EarlyBird is not null
            && venueNow < date.ToDateTime(StartsAt).AddMinutes(-EarlyBird.LeadMinutes)
                ? EarlyBird.DiscountOn(RegistrationFee)
                : 0m;

        return new OpenPlayPrice(RegistrationFee, discount, platformFee);
    }

    /// <summary>
    /// Ends the series after <paramref name="lastDate"/>. Every date after it is
    /// released for booking at once. The service must cancel any registrations
    /// on those dates first.
    /// </summary>
    public void End(DateOnly lastDate, DateTimeOffset now)
    {
        if (EndedAt is not null)
        {
            return;
        }

        // Ending before the start leaves a series with no dates, which is what
        // ending one that has not begun means.
        EndDate = EndDate is null || lastDate < EndDate ? lastDate : EndDate;
        EndedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Moves a draft to another court. The service keeps it at the same venue.
    /// </summary>
    public void MoveTo(Guid bookableCourtId, Guid courtId, DateTimeOffset now)
    {
        if (IsPublished || HasEnded)
        {
            throw new InvalidOperationException("Only a draft can be moved to another court.");
        }

        if (BookableCourtId == bookableCourtId)
        {
            return;
        }

        BookableCourtId = bookableCourtId;
        CourtId = courtId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Opens it for registration and holds the court for it. The service must
    /// check the court's bookings first: a clash is refused, never published
    /// on top of somebody's booking.
    /// </summary>
    public void Publish(Guid publishedByUserId, DateTimeOffset now)
    {
        if (HasEnded)
        {
            throw new InvalidOperationException("An open play that has ended cannot be published.");
        }

        if (IsPublished)
        {
            return;
        }

        PublishedAt = now;
        PublishedByUserId = publishedByUserId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Back to a draft: hidden again and the court released. The service must
    /// refuse this once anybody has registered, because their spot would
    /// disappear from under them.
    /// </summary>
    public void Unpublish(DateTimeOffset now)
    {
        if (HasEnded)
        {
            throw new InvalidOperationException("An open play that has ended cannot be unpublished.");
        }

        if (!IsPublished)
        {
            return;
        }

        PublishedAt = null;
        PublishedByUserId = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Sets or replaces the cover photo. Allowed on a published open play: the
    /// photo is not part of what a player signs up for, unlike the hours, the
    /// court and the price, which is why it is the one thing outside the lock.
    /// The service checks the URL is on the platform's own Cloudinary account.
    /// </summary>
    public void SetCoverPhoto(string publicId, string secureUrl, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(publicId) || string.IsNullOrWhiteSpace(secureUrl))
        {
            throw new ArgumentException("A cover photo needs its public id and its URL.");
        }

        if (HasEnded)
        {
            throw new InvalidOperationException("An open play that has ended cannot be changed.");
        }

        CoverPhotoPublicId = publicId.Trim();
        CoverPhotoUrl = secureUrl.Trim();
        UpdatedAt = now;
    }

    public void RemoveCoverPhoto(DateTimeOffset now)
    {
        if (HasEnded)
        {
            throw new InvalidOperationException("An open play that has ended cannot be changed.");
        }

        CoverPhotoPublicId = null;
        CoverPhotoUrl = null;
        UpdatedAt = now;
    }

    public void MarkSeeded(DateTimeOffset now)
    {
        SeededAt = now;
        UpdatedAt = now;
    }

    private void Apply(
        string title,
        string level,
        int maxPlayers,
        decimal registrationFee,
        TimeOnly startsAt,
        TimeOnly endsAt,
        OpenPlayDays days,
        DateOnly startDate,
        DateOnly? endDate,
        int registrationCutoffMinutes,
        OpenPlayEarlyBird? earlyBird)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("An open play needs a title.", nameof(title));
        }

        if (!OpenPlayLevel.IsSupported(level))
        {
            throw new ArgumentException($"'{level}' is not a supported level.", nameof(level));
        }

        if (maxPlayers < OpenPlayLimits.MinPlayers || maxPlayers > OpenPlayLimits.MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPlayers),
                $"An open play takes {OpenPlayLimits.MinPlayers} to {OpenPlayLimits.MaxPlayers} players.");
        }

        if (registrationFee < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(registrationFee), "A registration fee cannot be negative.");
        }

        if (endsAt <= startsAt)
        {
            throw new ArgumentException("An open play must end after it starts, on the same day.", nameof(endsAt));
        }

        if (days == OpenPlayDays.None)
        {
            throw new ArgumentException("Pick at least one day of the week.", nameof(days));
        }

        if (endDate is not null && endDate < startDate)
        {
            throw new ArgumentException("An open play cannot end before it starts.", nameof(endDate));
        }

        if (registrationCutoffMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(registrationCutoffMinutes), "The registration cut-off cannot be after the start.");
        }

        if (earlyBird is not null)
        {
            earlyBird.EnsureFits(registrationFee, registrationCutoffMinutes);
        }

        Title = title.Trim();
        Level = level;
        MaxPlayers = maxPlayers;
        RegistrationFee = registrationFee;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Days = days;
        StartDate = startDate;
        EndDate = endDate;
        RegistrationCutoffMinutes = registrationCutoffMinutes;
        EarlyBird = earlyBird;
    }
}

public static class OpenPlayStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Ended = "Ended";
}

public static class OpenPlayLimits
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 200;
    public const int TitleLimit = 150;

    /// <summary>An hour before the start, until a venue says otherwise.</summary>
    public const int DefaultCheckInLeadMinutes = 60;

    public const int LongestCheckInLeadMinutes = 24 * 60;
}

/// <summary>
/// Skill levels. Strings, like <see cref="BookableCourtKind"/>, so a value read
/// straight from the database means something.
/// </summary>
public static class OpenPlayLevel
{
    public const string AllLevels = "AllLevels";
    public const string Beginner = "Beginner";
    public const string Intermediate = "Intermediate";
    public const string Advanced = "Advanced";

    public static readonly IReadOnlyCollection<string> All = [AllLevels, Beginner, Intermediate, Advanced];

    public static bool IsSupported(string value) => All.Contains(value);
}
