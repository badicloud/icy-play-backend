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
