using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One thing a customer can book: a court set up for a sport, or one marked-out
/// part of it. A floor that takes basketball, volleyball, and pickleball three
/// across is five of these, and a booking points at one of them.
///
/// A row rather than a number worked out on the way past. The count is easy to
/// derive and derived is how names and status work everywhere else here — but a
/// booking needs something that cannot be recalculated out from under it. Once
/// part three is sold, re-marking the floor has to be a conversation rather
/// than a subtraction.
/// </summary>
public sealed class BookableCourt : Entity
{
    private BookableCourt()
    {
    }

    public BookableCourt(
        Guid courtSportId,
        Guid courtId,
        int divisionNumber,
        string kind,
        DateTimeOffset createdAt)
    {
        CourtSportId = courtSportId;
        CourtId = courtId;
        DivisionNumber = divisionNumber < 1 ? 1 : divisionNumber;
        Kind = kind;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// The court and sport this is one of. Carries the four rates and the
    /// division count, so neither is copied here.
    /// </summary>
    public Guid CourtSportId
    {
        get; private set;
    }
    public CourtSport CourtSport { get; private set; } = null!;

    /// <summary>
    /// The physical floor, denormalised off <see cref="CourtSport"/>.
    ///
    /// The one copy kept here, and it earns its place: every availability check
    /// asks what else is booked on this floor, and that is the most repeated
    /// question in the booking engine. Reaching it through the link table would
    /// put a join in front of every slot of every calendar.
    ///
    /// Nothing but the roster writes this, and it writes both columns from one
    /// read, which is what keeps the copy honest.
    /// </summary>
    public Guid CourtId
    {
        get; private set;
    }
    public Court Court { get; private set; } = null!;

    /// <summary>Which part this is. One when the court is played whole.</summary>
    public int DivisionNumber
    {
        get; private set;
    }

    /// <summary>
    /// Whole or divided: what the venue is selling, in one word.
    ///
    /// Stored, which is the exception to how the rest of this works. Part one of
    /// three and a court played whole both carry number one, so the moment the
    /// count changes — or this row is retired and the count moves on without it
    /// — nothing left on the row can tell the two apart.
    /// </summary>
    public string Kind { get; private set; } = BookableCourtKind.Whole;

    /// <summary>
    /// False once the floor is re-marked into fewer parts, or the sport is
    /// dropped. Retired rather than deleted, because a booking taken against it
    /// still has to point somewhere, and a receipt for a court that no longer
    /// exists is still a receipt.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public bool IsDivided => Kind == BookableCourtKind.Divided;

    /// <summary>
    /// Whether these two cannot be played at once.
    ///
    /// One floor hosts one sport at a time, and within that sport its parts run
    /// side by side. That is the whole rule:
    ///
    /// <list type="bullet">
    /// <item>different floors never clash;</item>
    /// <item>the same floor set up for a different sport always clashes — a
    /// basketball game and a pickleball game cannot share the markings;</item>
    /// <item>the same sport clashes only with itself: parts two and three stay
    /// free while part one is being played.</item>
    /// </list>
    ///
    /// Which parts of a floor physically overlap is deliberately not modelled.
    /// Three pickleball courts and two badminton courts marked on one basketball
    /// floor do not line up, and a model that claimed to know how would be wrong
    /// in a way nobody could see until two games were sold the same paint.
    /// </summary>
    public bool ConflictsWith(BookableCourt other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return CourtId == other.CourtId
            && ClashesWith(other.CourtSportId, other.DivisionNumber);
    }

    /// <summary>
    /// The same rule for a caller that has already narrowed to this floor and
    /// holds only ids. Availability reads every booking on a court for a day and
    /// has no entity to hand for each one.
    /// </summary>
    public bool ClashesWith(Guid courtSportId, int divisionNumber) =>
        CourtSportId != courtSportId || DivisionNumber == divisionNumber;

    /// <summary>
    /// Follows the division count when the floor is re-marked. Part one of a
    /// court that becomes three is the same bookable court it always was, now
    /// described differently.
    /// </summary>
    public void SetKind(string kind, DateTimeOffset now)
    {
        if (Kind == kind)
        {
            return;
        }

        Kind = kind;
        UpdatedAt = now;
    }

    public void Retire(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = now;
    }

    /// <summary>
    /// Brings back a part that was retired and has been marked out again. The
    /// same row, so a booking that survived the gap still resolves.
    /// </summary>
    public void Reinstate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAt = now;
    }
}

/// <summary>
/// Whether a bookable court is the whole floor or one part of it. Strings
/// rather than an enum, matching <see cref="ActivityKind"/> and
/// <see cref="CourtVenueType"/>: a value read straight out of the database is
/// worth more than one byte saved.
/// </summary>
public static class BookableCourtKind
{
    public const string Whole = "Whole";
    public const string Divided = "Divided";

    public static readonly IReadOnlyCollection<string> All = [Whole, Divided];

    public static bool IsSupported(string value) => All.Contains(value);

    /// <summary>
    /// What a part is called given how many the floor makes. The one place the
    /// question is answered, so the roster and the backfill cannot disagree.
    /// </summary>
    public static string For(int divisions) => divisions > 1 ? Divided : Whole;
}
