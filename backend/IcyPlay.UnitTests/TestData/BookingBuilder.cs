using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.UnitTests.TestData;

/// <summary>
/// A booking with hours on it, for the rules that read them.
///
/// The hours are the point: a booking with an empty <see cref="Booking.Slots"/>
/// answers "is this being played" with a flat no whatever the clock says, so a
/// test written against one proves nothing. This gives it a day and a run of
/// consecutive hours by default, and lets a test say otherwise.
/// </summary>
public sealed class BookingBuilder
{
    private DateOnly _date = new(2026, 1, 15);
    private TimeOnly _startsAt = new(13, 0);
    private int _hours = 3;
    private BookingStatus _status = BookingStatus.Confirmed;
    private readonly List<(TimeOnly StartsAt, TimeOnly EndsAt)> _explicitHours = [];

    public BookingBuilder On(DateOnly date)
    {
        _date = date;
        return this;
    }

    public BookingBuilder StartingAt(TimeOnly startsAt)
    {
        _startsAt = startsAt;
        return this;
    }

    public BookingBuilder LastingHours(int hours)
    {
        _hours = hours;
        return this;
    }

    public BookingBuilder WithStatus(BookingStatus status)
    {
        _status = status;
        return this;
    }

    /// <summary>
    /// Hours that need not run back to back — one o'clock and four o'clock with
    /// nothing bought in between, which is what an hourly booking is allowed to
    /// be and the reason the "is it on" rule looks at each hour.
    /// </summary>
    public BookingBuilder WithHours(params TimeOnly[] startTimes)
    {
        ArgumentNullException.ThrowIfNull(startTimes);

        _explicitHours.Clear();

        foreach (var startsAt in startTimes)
        {
            _explicitHours.Add((startsAt, startsAt.AddHours(1)));
        }

        return this;
    }

    public Booking Build()
    {
        var createdAt = TestTimes.UtcNow;

        var booking = new Booking(
            TestIds.For("bookable-court"),
            TestIds.CustomerUserId,
            BookingKind.Hourly,
            "Court 1 · Pickleball 1",
            "Demo Sports Center",
            "Pickleball",
            platformHourlyRate: 15m,
            startDate: _date,
            endDate: _date,
            holdMinutes: 30,
            createdAt: createdAt);

        var hours = _explicitHours.Count > 0
            ? _explicitHours
            : [.. Enumerable
                .Range(0, _hours)
                .Select(n => (_startsAt.AddHours(n), _startsAt.AddHours(n + 1)))];

        foreach (var (startsAt, endsAt) in hours)
        {
            booking.AddSlot(new BookingSlot(
                booking.Id,
                TestIds.CourtId,
                TestIds.For("bookable-court"),
                _date,
                startsAt,
                endsAt,
                CourtRateKind.Standard,
                amount: 300m,
                platformFee: 15m,
                createdAt: createdAt));
        }

        // Through the real transitions rather than by setting the field: a
        // booking that reached Confirmed any other way is not one the rules
        // under test will ever see.
        if (_status is BookingStatus.PendingVerification or BookingStatus.Confirmed)
        {
            booking.SubmitForVerification(createdAt);
        }

        switch (_status)
        {
            case BookingStatus.Confirmed:
                booking.Confirm(createdAt);
                break;
            case BookingStatus.Rejected:
                booking.Reject("Receipt did not match.", createdAt);
                break;
            case BookingStatus.Cancelled:
                booking.Cancel("Changed their mind.", createdAt);
                break;
            default:
                break;
        }

        return booking;
    }
}
