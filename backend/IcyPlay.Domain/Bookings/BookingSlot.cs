using IcyPlay.Domain.Common;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Domain.Bookings;

/// <summary>
/// One hour of one booking, priced on its own.
///
/// A booking is not billed as a block because its hours need not cost the same:
/// six to seven in the morning and six to seven in the evening are the standard
/// and the peak rate, and a customer who books both is owed a bill that says so
/// rather than an average.
/// </summary>
public sealed class BookingSlot : Entity
{
    private BookingSlot()
    {
    }

    public BookingSlot(
        Guid bookingId,
        Guid courtId,
        Guid bookableCourtId,
        DateOnly date,
        TimeOnly startsAt,
        TimeOnly endsAt,
        CourtRateKind rateKind,
        decimal amount,
        decimal platformFee,
        DateTimeOffset createdAt)
    {
        BookingId = bookingId;
        CourtId = courtId;
        BookableCourtId = bookableCourtId;
        Date = date;
        StartsAt = startsAt;
        EndsAt = endsAt;
        RateKind = rateKind;
        Amount = amount;
        PlatformFee = platformFee;
        CreatedAt = createdAt;
    }

    public Guid BookingId
    {
        get; private set;
    }
    public Booking Booking { get; private set; } = null!;

    /// <summary>
    /// The physical floor, denormalised off the booking's bookable court.
    ///
    /// Availability asks one question, over and over: what is taken on this
    /// floor, on this date. This column with the date beside it is what makes
    /// that a single seek rather than a walk through two joins for every hour of
    /// every calendar anyone opens.
    /// </summary>
    public Guid CourtId
    {
        get; private set;
    }

    /// <summary>
    /// Which part of that floor this hour was sold as.
    ///
    /// On the hour rather than on the booking, because a booking can change
    /// court half way through: a court that fails at two o'clock leaves the
    /// morning on one and the afternoon on another. Reading it off the booking
    /// would say the morning had been played somewhere it never was, and the
    /// clash rule — basketball blocking all three pickleball courts — would be
    /// asked about the wrong one.
    /// </summary>
    public Guid BookableCourtId
    {
        get; private set;
    }
    public BookableCourt BookableCourt { get; private set; } = null!;

    public DateOnly Date
    {
        get; private set;
    }
    public TimeOnly StartsAt
    {
        get; private set;
    }
    public TimeOnly EndsAt
    {
        get; private set;
    }

    /// <summary>Which rate this hour fell under, so a receipt can say why.</summary>
    public CourtRateKind RateKind
    {
        get; private set;
    }

    /// <summary>The court rental for this hour, as priced when it was booked.</summary>
    public decimal Amount
    {
        get; private set;
    }

    /// <summary>The platform's per-hour fee, as it stood when it was booked.</summary>
    public decimal PlatformFee
    {
        get; private set;
    }

    /// <summary>
    /// Whether this hour overlaps another. Times are wall-clock within one
    /// date and the venue's hours do not run past midnight, so neither does
    /// this: touching ends do not overlap, or back-to-back hours would each
    /// block the next.
    /// </summary>
    public bool Overlaps(DateOnly date, TimeOnly startsAt, TimeOnly endsAt) =>
        Date == date && StartsAt < endsAt && startsAt < EndsAt;

    /// <summary>
    /// Whether this hour has begun, on the venue's wall clock.
    ///
    /// What "already played, and so staying put" means everywhere a booking is
    /// moved: the hour running as somebody presses Move is half spent on the
    /// court they are standing on, and carrying it elsewhere would sell the
    /// whole of it again somewhere they can only have the rest of it.
    ///
    /// Here rather than written out at each call site, because it was written
    /// out twice and the two did not agree. One counted an hour played once it
    /// had FINISHED, the other once it had BEGUN — so a customer's upgrade was
    /// recorded against one set of hours and the desk checked it against a
    /// different one, and every approval made mid-session was refused as no
    /// longer adding up.
    ///
    /// <paramref name="venueNow"/> is wall-clock time AT THE VENUE. A slot is
    /// "two o'clock on the 22nd" at that building; handing this UTC in Manila
    /// would answer for eight hours ago.
    /// </summary>
    public bool HasBegunAt(DateTime venueNow) => Date.ToDateTime(StartsAt) <= venueNow;
}
