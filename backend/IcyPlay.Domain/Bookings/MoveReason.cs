using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Bookings;

/// <summary>
/// Why a customer moved their booking, from a short fixed list.
///
/// A list rather than a free box, because the point of asking is to count the
/// answers. "rain", "raining" and "ulan" typed into a box are three answers to
/// a report and one answer to a person; picked from a list they are one.
/// <see cref="Other"/> is the way out, and asks for a few words so it is not
/// a way of saying nothing.
///
/// Strings rather than an enum, matching <see cref="BookingKind"/>: they are
/// stored as they are named, and a report that reads them back does not need
/// the domain to translate.
/// </summary>
public static class MoveReason
{
    public const string ScheduleChanged = "ScheduleChanged";
    public const string Weather = "Weather";
    public const string CourtProblem = "CourtProblem";
    public const string DifferentCourt = "DifferentCourt";
    public const string Other = "Other";

    public static readonly IReadOnlyCollection<string> All =
        [ScheduleChanged, Weather, CourtProblem, DifferentCourt, Other];

    /// <summary>
    /// Long enough to say what "other" was, short enough that it stays a reason
    /// rather than becoming a letter to the venue.
    /// </summary>
    public const int NoteLimit = 200;

    public static bool IsSupported(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>
    /// How the reason reads in a booking's history, which the customer and the
    /// desk both read.
    /// </summary>
    public static string Label(string reason) => reason switch
    {
        ScheduleChanged => "Schedule changed",
        Weather => "Weather",
        CourtProblem => "Problem with the court",
        DifferentCourt => "Wanted a different court",
        Other => "Other",
        _ => reason
    };
}

/// <summary>
/// One move that went through: a free one the moment it was asked for, or an
/// upgrade the moment the desk approved it.
///
/// Its own row rather than read back out of the audit trail, because a report
/// asks this table for counts by week and by reason, and the audit trail is a
/// record of what happened rather than something built to be counted. The
/// trail still gets its entry; this is the tally.
/// </summary>
public sealed class BookingMoveRecord : Entity
{
    private BookingMoveRecord()
    {
    }

    public BookingMoveRecord(
        Guid bookingId,
        DateTimeOffset movedAt,
        string kind,
        string? reason,
        string? reasonNote,
        string? fromCourtName,
        string? toCourtName,
        Guid? movedByUserId)
    {
        BookingId = bookingId;
        MovedAt = movedAt;
        Kind = kind;
        Reason = reason;
        ReasonNote = string.IsNullOrWhiteSpace(reasonNote) ? null : reasonNote.Trim();
        FromCourtName = fromCourtName;
        ToCourtName = toCourtName;
        MovedByUserId = movedByUserId;
        CreatedAt = movedAt;
    }

    public Guid BookingId
    {
        get; private set;
    }
    public Booking Booking { get; private set; } = null!;

    public DateTimeOffset MovedAt
    {
        get; private set;
    }

    /// <summary><see cref="MoveKind.Free"/> or <see cref="MoveKind.Upgrade"/>.</summary>
    public string Kind { get; private set; } = MoveKind.Free;

    /// <summary>
    /// One of <see cref="MoveReason"/>. Null on a move made before customers
    /// were asked, which a report shows as not asked rather than guessing.
    /// </summary>
    public string? Reason
    {
        get; private set;
    }

    public string? ReasonNote
    {
        get; private set;
    }

    /// <summary>Where it was and where it went, as they were named then. Null on the old ones.</summary>
    public string? FromCourtName
    {
        get; private set;
    }

    public string? ToCourtName
    {
        get; private set;
    }

    public Guid? MovedByUserId
    {
        get; private set;
    }
}

public static class MoveKind
{
    /// <summary>The same price or cheaper, and done the moment it was asked.</summary>
    public const string Free = "Free";

    /// <summary>Dearer, paid for, and done when the desk approved it.</summary>
    public const string Upgrade = "Upgrade";
}
