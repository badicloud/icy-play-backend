using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Common;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// The venue's side of a booking.
///
/// Every read and every write starts from the venues this person works, never
/// from a facility id the caller hands over. An owner works what they own and an
/// attendant works what they are on: which desk somebody is standing at is not
/// theirs to claim.
/// </summary>
public sealed class DeskService(
    AppDbContext db,
    IBookingNotifier notifier,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<DeskService> logger) : IDeskService
{
    private const int LargestPage = 50;

    /// <summary>
    /// A month and a day, so a calendar can ask for the whole of a month it is
    /// showing plus the edges of the weeks either side of it.
    /// </summary>
    private const int WidestWindowInDays = 42;

    public async Task<IReadOnlyCollection<DeskVenue>> VenuesAsync(Guid userId, CancellationToken ct) =>
        await VenueQuery(userId)
            .AsNoTracking()
            .OrderBy(facility => facility.Name)
            .Select(facility => new DeskVenue(facility.Id, facility.Name))
            .ToListAsync(ct);

    public async Task<DeskResult<PagedResult<DeskBooking>>> ListAsync(
        Guid userId,
        DeskQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!DeskTab.IsSupported(query.Tab))
        {
            return DeskResult<PagedResult<DeskBooking>>.Fail(DeskFailure.UnknownTab);
        }

        var venueIds = await VenueQuery(userId)
            .AsNoTracking()
            .Select(facility => facility.Id)
            .ToListAsync(ct);

        if (query.FacilityId is Guid wanted)
        {
            if (!venueIds.Contains(wanted))
            {
                return DeskResult<PagedResult<DeskBooking>>.Fail(DeskFailure.NotAttended);
            }

            venueIds = [wanted];
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, LargestPage);

        var status = query.Tab == DeskTab.Waiting
            ? BookingStatus.PendingVerification
            : BookingStatus.Confirmed;

        var rows = db.Bookings
            .AsNoTracking()
            .Where(booking => booking.Status == status
                && venueIds.Contains(booking.BookableCourt.Court.FacilityId));

        var total = await rows.CountAsync(ct);

        // What is waiting is ordered by how long it has been waiting, oldest
        // first: somebody who paid an hour ago should not be behind somebody who
        // paid a minute ago. What is confirmed is history, and history reads
        // newest first.
        var ordered = query.Tab == DeskTab.Waiting
            ? rows.OrderBy(booking => booking.SubmittedForVerificationAt)
            : rows.OrderByDescending(booking => booking.ConfirmedAt);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(booking => new Row(
                booking,
                booking.BookableCourt.Court.FacilityId,
                booking.BookableCourt.CourtId,
                booking.BookableCourt.DivisionNumber,
                booking.BookableCourt.CourtSport.Sport.Key,
                db.Users
                    .Where(user => user.Id == booking.CustomerUserId)
                    .Select(user => new Person(user.FullName, user.Email, user.PhoneNumber))
                    .FirstOrDefault(),
                booking.Slots.ToList()))
            .ToListAsync(ct);

        return DeskResult<PagedResult<DeskBooking>>.Success(
            new PagedResult<DeskBooking>([.. items.Select(Detail)], page, pageSize, total));
    }

    public async Task<IReadOnlyCollection<DeskCourt>> CourtsAsync(Guid userId, CancellationToken ct)
    {
        var venueIds = await VenueQuery(userId)
            .AsNoTracking()
            .Select(facility => facility.Id)
            .ToListAsync(ct);

        var rows = await db.Courts
            .AsNoTracking()
            .Where(court => court.IsActive && venueIds.Contains(court.FacilityId))
            .OrderBy(court => court.Facility.Name)
            .ThenBy(court => court.DisplayOrder)
            .ThenBy(court => court.Name)
            .Select(court => new
            {
                court.Id,
                court.FacilityId,
                FacilityName = court.Facility.Name,
                court.Name,
                // A court sport is live by existing; only the parts it is
                // marked out into are retired and brought back.
                Units = court.Sports
                    .SelectMany(pair => pair.BookableCourts
                        .Where(unit => unit.IsActive)
                        .Select(unit => new
                        {
                            BookableCourtId = unit.Id,
                            SportName = pair.Sport.Name,
                            SportKey = pair.Sport.Key,
                            unit.DivisionNumber,
                            pair.Divisions
                        }))
                    .ToList()
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(court => new DeskCourt(
                court.Id,
                court.FacilityId,
                court.FacilityName,
                court.Name,
                [
                    .. court.Units
                        .OrderBy(unit => unit.SportName)
                        .ThenBy(unit => unit.DivisionNumber)
                        .Select(unit => new DeskCourtUnit(
                            unit.BookableCourtId,
                            unit.SportName,
                            unit.SportKey,
                            unit.DivisionNumber,
                            UnitLabel(unit.SportName, unit.DivisionNumber, unit.Divisions)))
                ]))
        ];
    }

    public async Task<DeskResult<IReadOnlyCollection<ScheduleEntry>>> ScheduleAsync(
        Guid userId,
        Guid courtId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (!await WorksThisCourtAsync(userId, courtId, ct))
        {
            return DeskResult<IReadOnlyCollection<ScheduleEntry>>.Fail(DeskFailure.NotAttended);
        }

        if (to < from || to.DayNumber - from.DayNumber > WidestWindowInDays)
        {
            // A diary is drawn a month at a time. Anything wider is a report,
            // and a report should not arrive one hour per row.
            return DeskResult<IReadOnlyCollection<ScheduleEntry>>.Fail(DeskFailure.WindowTooWide);
        }

        var now = timeProvider.GetUtcNow();

        var rows = await db.BookingSlots
            .AsNoTracking()
            .Where(slot => slot.CourtId == courtId && slot.Date >= from && slot.Date <= to)
            .Select(slot => new
            {
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.Booking.Id,
                slot.Booking.Status,
                slot.Booking.HoldsUntil,
                slot.Booking.ReceiptUrl,
                slot.Booking.BookableCourtId,
                slot.Booking.BookableCourt.DivisionNumber,
                SportName = slot.Booking.BookableCourt.CourtSport.Sport.Name,
                SportKey = slot.Booking.BookableCourt.CourtSport.Sport.Key,
                slot.Booking.BookableCourt.CourtSport.Divisions,
                CustomerName = db.Users
                    .Where(user => user.Id == slot.Booking.CustomerUserId)
                    .Select(user => user.FullName)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return DeskResult<IReadOnlyCollection<ScheduleEntry>>.Success(
        [
            .. rows
                // Only what still holds the hour. A hold whose clock ran out
                // without a receipt has let go, and drawing it as taken would
                // send somebody away from an hour they could have sold.
                .Where(row => BookingStatuses.IsLive(row.Status)
                    && !(row.Status == BookingStatus.PendingPayment
                        && row.ReceiptUrl == null
                        && now >= row.HoldsUntil))
                .OrderBy(row => row.Date)
                .ThenBy(row => row.StartsAt)
                .Select(row => new ScheduleEntry(
                    row.Id,
                    courtId,
                    row.BookableCourtId,
                    UnitLabel(row.SportName, row.DivisionNumber, row.Divisions),
                    row.SportKey,
                    row.Status.ToString(),
                    row.CustomerName ?? "Unknown customer",
                    row.Date,
                    row.StartsAt,
                    row.EndsAt))
        ]);
    }

    public async Task<DeskResult<PagedResult<DeskBooking>>> CourtBookingsAsync(
        Guid userId,
        CourtBookingQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await WorksThisCourtAsync(userId, query.CourtId, ct))
        {
            return DeskResult<PagedResult<DeskBooking>>.Fail(DeskFailure.NotAttended);
        }

        BookingStatus? wanted = null;

        if (query.Status is not null)
        {
            if (!Enum.TryParse<BookingStatus>(query.Status, ignoreCase: true, out var parsed))
            {
                return DeskResult<PagedResult<DeskBooking>>.Fail(DeskFailure.UnknownStatus);
            }

            wanted = parsed;
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, LargestPage);

        var rows = db.Bookings
            .AsNoTracking()
            .Where(booking => booking.BookableCourt.CourtId == query.CourtId);

        if (wanted is BookingStatus status)
        {
            rows = rows.Where(booking => booking.Status == status);
        }

        if (query.From is DateOnly from)
        {
            // Overlapping, not starting within: a run of days that began before
            // the window is still on the court during it.
            rows = rows.Where(booking => booking.EndDate >= from);
        }

        if (query.To is DateOnly to)
        {
            rows = rows.Where(booking => booking.StartDate <= to);
        }

        var total = await rows.CountAsync(ct);

        var items = await rows
            .OrderByDescending(booking => booking.StartDate)
            .ThenByDescending(booking => booking.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(booking => new Row(
                booking,
                booking.BookableCourt.Court.FacilityId,
                booking.BookableCourt.CourtId,
                booking.BookableCourt.DivisionNumber,
                booking.BookableCourt.CourtSport.Sport.Key,
                db.Users
                    .Where(user => user.Id == booking.CustomerUserId)
                    .Select(user => new Person(user.FullName, user.Email, user.PhoneNumber))
                    .FirstOrDefault(),
                booking.Slots.ToList()))
            .ToListAsync(ct);

        return DeskResult<PagedResult<DeskBooking>>.Success(
            new PagedResult<DeskBooking>([.. items.Select(Detail)], page, pageSize, total));
    }

    public async Task<DeskResult<DeskBooking>> BookingAsync(
        Guid userId,
        Guid bookingId,
        CancellationToken ct)
    {
        var venueIds = VenueQuery(userId).Select(facility => facility.Id);

        var here = await db.Bookings
            .AsNoTracking()
            .AnyAsync(
                booking => booking.Id == bookingId
                    && venueIds.Contains(booking.BookableCourt.Court.FacilityId),
                ct);

        return here
            ? DeskResult<DeskBooking>.Success(await OneAsync(bookingId, ct))
            : DeskResult<DeskBooking>.Fail(DeskFailure.BookingNotFound);
    }

    public async Task<DeskResult<DeskBooking>> ConfirmAsync(
        Guid userId,
        Guid bookingId,
        AuditActor actor,
        CancellationToken ct)
    {
        var booking = await ForDeskAsync(userId, bookingId, ct);

        if (booking is null)
        {
            return DeskResult<DeskBooking>.Fail(DeskFailure.BookingNotFound);
        }

        if (booking.Status != BookingStatus.PendingVerification)
        {
            // Two people at one desk, both pressing. The first press stands.
            return DeskResult<DeskBooking>.Fail(DeskFailure.NotWaiting);
        }

        if (booking.ReceiptUrl is null)
        {
            return DeskResult<DeskBooking>.Fail(DeskFailure.NoReceipt);
        }

        booking.Confirm(timeProvider.GetUtcNow());
        Record(actor, AuditAction.BookingConfirmed, booking, null);
        await db.SaveChangesAsync(ct);

        // After the save, and best effort: a mail provider being down must not
        // undo a confirmation somebody has already given.
        await TellTheCustomerAsync(booking, ct);

        return DeskResult<DeskBooking>.Success(await OneAsync(bookingId, ct));
    }

    public async Task<DeskResult<DeskBooking>> RejectAsync(
        Guid userId,
        Guid bookingId,
        string? reason,
        AuditActor actor,
        CancellationToken ct)
    {
        var booking = await ForDeskAsync(userId, bookingId, ct);

        if (booking is null)
        {
            return DeskResult<DeskBooking>.Fail(DeskFailure.BookingNotFound);
        }

        if (booking.Status != BookingStatus.PendingVerification)
        {
            return DeskResult<DeskBooking>.Fail(DeskFailure.NotWaiting);
        }

        booking.Reject(reason, timeProvider.GetUtcNow());
        Record(actor, AuditAction.BookingRejected, booking, reason);
        await db.SaveChangesAsync(ct);

        // No letter yet. A rejection needs somewhere for the customer to answer
        // from, and that is the message thread, which is not built. Telling
        // somebody their payment was refused and leaving them no reply is worse
        // than the console showing it and somebody ringing them.
        logger.LogInformation(
            "Booking {BookingId} was rejected at the desk. The customer has not been emailed.",
            bookingId);

        return DeskResult<DeskBooking>.Success(await OneAsync(bookingId, ct));
    }

    /// <summary>
    /// Whether this court sits in a venue they work. Asked before every read of
    /// one court, so a court id from elsewhere answers the same as a made-up one.
    /// </summary>
    private async Task<bool> WorksThisCourtAsync(Guid userId, Guid courtId, CancellationToken ct)
    {
        var venueIds = VenueQuery(userId).Select(facility => facility.Id);

        return await db.Courts
            .AsNoTracking()
            .AnyAsync(court => court.Id == courtId && venueIds.Contains(court.FacilityId), ct);
    }

    /// <summary>
    /// What one part of a court is called: the sport on a whole floor, the sport
    /// and the number on a divided one. Derived, never stored — a stored name
    /// would outlive the marking out that made it true.
    /// </summary>
    private static string UnitLabel(string sportName, int divisionNumber, int divisions) =>
        divisions <= 1 ? sportName : $"{sportName} {divisionNumber}";

    /// <summary>
    /// The venues this person works: the ones they own, and the ones they are
    /// on the desk of today.
    /// </summary>
    private IQueryable<Domain.Facilities.Facility> VenueQuery(Guid userId) =>
        db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant =>
                    attendant.UserId == userId && attendant.IsActive));

    /// <summary>
    /// The booking, tracked, and only if it belongs to a venue this person
    /// works. A booking at somebody else's venue answers the same as one that
    /// is not there, so the desk cannot be used to map what it cannot see.
    /// </summary>
    private async Task<Booking?> ForDeskAsync(Guid userId, Guid bookingId, CancellationToken ct)
    {
        var venueIds = VenueQuery(userId).Select(facility => facility.Id);

        return await db.Bookings
            .Include(booking => booking.Slots)
            .SingleOrDefaultAsync(
                booking => booking.Id == bookingId
                    && venueIds.Contains(booking.BookableCourt.Court.FacilityId),
                ct);
    }

    private async Task<DeskBooking> OneAsync(Guid bookingId, CancellationToken ct)
    {
        var row = await db.Bookings
            .AsNoTracking()
            .Where(booking => booking.Id == bookingId)
            .Select(booking => new Row(
                booking,
                booking.BookableCourt.Court.FacilityId,
                booking.BookableCourt.CourtId,
                booking.BookableCourt.DivisionNumber,
                booking.BookableCourt.CourtSport.Sport.Key,
                db.Users
                    .Where(user => user.Id == booking.CustomerUserId)
                    .Select(user => new Person(user.FullName, user.Email, user.PhoneNumber))
                    .FirstOrDefault(),
                booking.Slots.ToList()))
            .SingleAsync(ct);

        return Detail(row);
    }

    private async Task TellTheCustomerAsync(Booking booking, CancellationToken ct)
    {
        try
        {
            await notifier.BookingConfirmedAsync(booking, ct);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The confirmation letter for booking {BookingId} could not be sent.",
                booking.Id);
        }
    }

    private void Record(AuditActor actor, string action, Booking booking, string? reason) =>
        audit.RecordEvent(
            actor,
            action,
            AuditEntityType.Booking,
            booking.Id,
            new Dictionary<string, string?>
            {
                ["court"] = booking.CourtName,
                ["dates"] = booking.StartDate == booking.EndDate
                    ? booking.StartDate.ToString("yyyy-MM-dd")
                    : $"{booking.StartDate:yyyy-MM-dd} to {booking.EndDate:yyyy-MM-dd}",
                ["total"] = booking.Total.ToString("0.00")
            },
            reason);

    /// <summary>Stands in for a customer whose account has gone.</summary>
    private static readonly Person NobodyKnown = new("Unknown customer", "", null);

    private static DeskBooking Detail(Row row) => Detail(row, row.Slots);

    private static DeskBooking Detail(Row row, IReadOnlyCollection<BookingSlot> slots) => new(
        row.Booking.Id,
        row.FacilityId,
        row.Booking.FacilityName,
        row.CourtId,
        row.Booking.BookableCourtId,
        row.DivisionNumber,
        row.Booking.CourtName,
        row.Booking.SportName,
        row.SportKey,
        row.Booking.Kind,
        row.Booking.Status.ToString(),
        (row.Customer ?? NobodyKnown).FullName,
        (row.Customer ?? NobodyKnown).Email,
        (row.Customer ?? NobodyKnown).PhoneNumber,
        row.Booking.StartDate,
        row.Booking.EndDate,
        slots.Count,
        slots.Sum(slot => slot.Amount),
        slots.Sum(slot => slot.PlatformFee),
        slots.Sum(slot => slot.Amount + slot.PlatformFee),
        row.Booking.ReceiptUrl,
        row.Booking.ReceiptUploadedAt,
        row.Booking.SubmittedForVerificationAt,
        row.Booking.ConfirmedAt,
        row.Booking.CancellationReason,
        [
            .. slots
                .OrderBy(slot => slot.Date)
                .ThenBy(slot => slot.StartsAt)
                .Select(slot => new BookedSlot(
                    slot.Date,
                    slot.StartsAt,
                    slot.EndsAt,
                    slot.RateKind.ToString(),
                    slot.Amount,
                    slot.PlatformFee))
        ],
        row.Booking.CreatedAt);

    /// <summary>
    /// A booking with the few things around it the desk needs: which venue it
    /// belongs to, the sport's key for artwork, and who booked it.
    /// </summary>
    private sealed record Row(
        Booking Booking,
        Guid FacilityId,
        Guid CourtId,
        int DivisionNumber,
        string SportKey,
        /// <summary>Null when the account behind the booking has gone.</summary>
        Person? Customer,
        /// <summary>
        /// The hours, asked for in the projection. The booking's own collection
        /// is not filled in by a query that projects, and the totals are read
        /// off these.
        /// </summary>
        IReadOnlyCollection<BookingSlot> Slots);

    /// <summary>Who booked it. Checked against the name on the GCash receipt.</summary>
    private sealed record Person(string FullName, string Email, string? PhoneNumber);
}
