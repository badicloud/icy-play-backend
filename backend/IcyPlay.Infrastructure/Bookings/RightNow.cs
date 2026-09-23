using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// What the venue looks like at this moment: how much of it there is, and how
/// much of it has somebody on it.
///
/// Asked on each venue's own clock. A desk with two buildings in two zones is
/// unusual but not impossible, and "is this hour running" is a question only
/// the venue's wall clock can answer.
/// </summary>
internal static class RightNow
{
    public static async Task<VenueSnapshot> ReadAsync(
        AppDbContext db,
        IReadOnlyCollection<Guid> venueIds,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var courts = await db.Courts
            .AsNoTracking()
            .CountAsync(court => court.IsActive && venueIds.Contains(court.FacilityId), ct);

        var units = await db.BookableCourts
            .AsNoTracking()
            .Where(unit =>
                unit.IsActive
                && unit.Court.IsActive
                && venueIds.Contains(unit.Court.FacilityId))
            .Select(unit => new
            {
                unit.Id,
                unit.CourtId,
                unit.Court.FacilityId
            })
            .ToListAsync(ct);

        if (units.Count == 0)
        {
            return new VenueSnapshot(courts, 0, 0, 0, 0);
        }

        // In force at this instant, which needs no clock of anybody's: a
        // closure is stored as an absolute moment, not as a wall-clock one.
        var closed = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period =>
                period.LiftedAt == null
                && period.StartsAt <= utcNow
                && (period.EndsAt == null || utcNow < period.EndsAt)
                && venueIds.Contains(period.FacilityId))
            .Select(period => new { period.CourtId, period.FacilityId })
            .ToListAsync(ct);

        var shutCourts = closed
            .Where(period => period.CourtId is not null)
            .Select(period => period.CourtId!.Value)
            .ToHashSet();

        var shutVenues = closed
            .Where(period => period.CourtId is null)
            .Select(period => period.FacilityId)
            .ToHashSet();

        var clocks = await db.Facilities
            .AsNoTracking()
            .Where(facility => venueIds.Contains(facility.Id))
            .Select(facility => new { facility.Id, facility.TimeZone })
            .ToDictionaryAsync(facility => facility.Id, facility => facility.TimeZone, ct);

        // A day either side of UTC's own date, because a venue's local date can
        // be either of them. Narrowed to the hour that is actually running once
        // each facility's clock is known.
        var utcToday = DateOnly.FromDateTime(utcNow.UtcDateTime);

        var slots = await db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                venueIds.Contains(slot.BookableCourt.Court.FacilityId)
                && slot.Date >= utcToday.AddDays(-1)
                && slot.Date <= utcToday.AddDays(1)
                && (slot.Booking.Status == BookingStatus.Confirmed
                    || slot.Booking.Status == BookingStatus.PendingVerification
                    || slot.Booking.Status == BookingStatus.PendingPayment))
            .Select(slot => new Running(
                slot.BookableCourtId,
                slot.BookableCourt.Court.FacilityId,
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.Booking.Status,
                slot.Booking.HoldsUntil,
                slot.Booking.ReceiptUrl))
            .ToListAsync(ct);

        var taken = new HashSet<Guid>();

        foreach (var slot in slots)
        {
            var venueNow = VenueClock.LocalNowIn(
                clocks.GetValueOrDefault(slot.FacilityId),
                utcNow);

            var date = DateOnly.FromDateTime(venueNow.DateTime);
            var time = TimeOnly.FromDateTime(venueNow.DateTime);

            // The end is exclusive, matching how a slot is compared everywhere
            // else: at exactly two o'clock the one o'clock hour is over, or two
            // consecutive hours would both claim the same instant.
            var running = slot.Date == date && slot.StartsAt <= time && time < slot.EndsAt;

            if (running && Holds(slot, utcNow))
            {
                taken.Add(slot.BookableCourtId);
            }
        }

        // Sorted once, in one order, so the three come to the whole. A part
        // closed for work is counted there whether or not somebody had it
        // booked: the venue has shut it, and a maintenance figure that left
        // those out would understate the closure they actually made.
        //
        // Only the parts still on the books are sorted at all. A booking on a
        // part the venue has since retired does not make a court it no longer
        // sells look busy.
        var maintenance = 0;
        var booked = 0;

        foreach (var unit in units)
        {
            if (shutCourts.Contains(unit.CourtId) || shutVenues.Contains(unit.FacilityId))
            {
                maintenance += 1;
            }
            else if (taken.Contains(unit.Id))
            {
                booked += 1;
            }
        }

        return new VenueSnapshot(
            courts,
            units.Count,
            units.Count - booked - maintenance,
            booked,
            maintenance);
    }

    /// <summary>
    /// Whether this booking still holds its court.
    ///
    /// The same rule <see cref="Booking.HoldsTheCourtAt"/> states, asked of a
    /// row rather than of an entity: an unpaid hold whose clock has run out has
    /// let go, and a court it is sitting on is free to sell. Counting it as
    /// booked would have the desk turning somebody away from an hour anybody
    /// can buy.
    /// </summary>
    private static bool Holds(Running slot, DateTimeOffset utcNow) =>
        slot.Status != BookingStatus.PendingPayment
        || slot.ReceiptUrl is not null
        || utcNow < slot.HoldsUntil;

    private sealed record Running(
        Guid BookableCourtId,
        Guid FacilityId,
        DateOnly Date,
        TimeOnly StartsAt,
        TimeOnly EndsAt,
        BookingStatus Status,
        DateTimeOffset HoldsUntil,
        string? ReceiptUrl);
}
