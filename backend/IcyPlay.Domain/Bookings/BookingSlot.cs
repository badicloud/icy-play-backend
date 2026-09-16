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
}
