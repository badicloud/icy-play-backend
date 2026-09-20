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

    public async Task<DeskResult<PagedResult<DeskUpgrade>>> UpgradesAsync(
        Guid userId,
        DeskUpgradeQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!DeskUpgradeTab.IsSupported(query.Tab))
        {
            return DeskResult<PagedResult<DeskUpgrade>>.Fail(DeskFailure.UnknownTab);
        }

        var venueIds = await VenueQuery(userId)
            .AsNoTracking()
            .Select(facility => facility.Id)
            .ToListAsync(ct);

        if (query.FacilityId is Guid wanted)
        {
            if (!venueIds.Contains(wanted))
            {
                return DeskResult<PagedResult<DeskUpgrade>>.Fail(DeskFailure.NotAttended);
            }

            venueIds = [wanted];
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, LargestPage);

        // Only what the customer has actually sent. One still waiting to be paid
        // for is nobody's work at the desk, and putting it in this queue would
        // have somebody checking for a receipt that is not coming yet.
        var rows = query.Tab == DeskUpgradeTab.Waiting
            ? db.BookingUpgradeRequests.Where(row => row.Status == UpgradeStatus.AwaitingApproval)
            : db.BookingUpgradeRequests.Where(row =>
                row.Status == UpgradeStatus.Approved || row.Status == UpgradeStatus.Declined);

        rows = rows.AsNoTracking()
            .Where(row => venueIds.Contains(row.Booking.BookableCourt.Court.FacilityId));

        var total = await rows.CountAsync(ct);

        // Waiting is ordered by how long it has been waiting, oldest first:
        // somebody who paid an hour ago should not be behind somebody who paid
        // a minute ago. Settled is history, and history reads newest first.
        var ordered = query.Tab == DeskUpgradeTab.Waiting
            ? rows.OrderBy(row => row.ReceiptUploadedAt).ThenBy(row => row.CreatedAt)
            : rows.OrderByDescending(row => row.SettledAt);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new UpgradeRow(
                row,
                row.Booking,
                row.Booking.BookableCourt.Court.FacilityId,
                row.Booking.BookableCourt.Court.Facility.Name,
                row.Booking.BookableCourt.CourtSport.Sport.Name,
                row.Booking.BookableCourt.CourtSport.Sport.Key,
                db.Users
                    .Where(user => user.Id == row.RequestedByUserId)
                    .Select(user => new Person(user.FullName, user.Email, user.PhoneNumber))
                    .FirstOrDefault(),
                row.Booking.Slots.ToList(),
                row.Slots.ToList()))
            .ToListAsync(ct);

        return DeskResult<PagedResult<DeskUpgrade>>.Success(
            new PagedResult<DeskUpgrade>([.. items.Select(Upgrade)], page, pageSize, total));
    }

    public async Task<DeskResult<DeskUpgrade>> ApproveUpgradeAsync(
        Guid userId,
        Guid upgradeId,
        AuditActor actor,
        CancellationToken ct)
    {
        var upgrade = await ForDeskUpgradeAsync(userId, upgradeId, ct);

        if (upgrade is null)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeNotFound);
        }

        // Two people at one desk, both pressing. The first press stands.
        if (upgrade.Status != UpgradeStatus.AwaitingApproval)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeNotWaiting);
        }

        if (upgrade.ReceiptUrl is null)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.NoReceipt);
        }

        var booking = upgrade.Booking;
        var utcNow = timeProvider.GetUtcNow();

        var target = await db.BookableCourts
            .Include(unit => unit.Court)
            .SingleOrDefaultAsync(unit => unit.Id == upgrade.ToBookableCourtId, ct);

        if (target is null)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeNotFound);
        }

        var venueNow = await VenueNowAsync(target.Court.FacilityId, utcNow, ct);

        // Hours already played stay where they were played. Worked out again
        // here rather than trusted from the quote, because time has passed
        // since — and if it has passed far enough that the swap no longer adds
        // up, saying so beats guessing which hours the customer meant.
        var played = booking.Slots
            .Where(slot => slot.Date.ToDateTime(slot.EndsAt) <= venueNow.DateTime)
            .ToArray();

        if (played.Length + upgrade.Slots.Count != booking.Slots.Count)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeStale);
        }

        if (await IsTakenAsync(target, booking.Id, [.. upgrade.Slots], utcNow, ct))
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeHoursTaken);
        }

        // Priced as it was quoted, not as the court costs today. The customer
        // has already paid against that figure, and a rate the venue changed in
        // between must not change what they bought.
        var moved = upgrade.Slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .Select(slot => new BookingSlot(
                booking.Id,
                target.CourtId,
                target.Id,
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.RateKind,
                slot.Amount,
                slot.PlatformFee,
                utcNow))
            .ToArray();

        var wasOn = booking.CourtName;

        db.BookingSlots.RemoveRange(booking.Slots.Except(played).ToArray());
        booking.MoveTo(target.Id, upgrade.ToCourtName, played, moved, countsAgainstTheLimit: true, utcNow);
        db.BookingSlots.AddRange(moved);
        booking.Settle(upgrade.BalanceDue, utcNow);

        upgrade.Approve(userId, utcNow);

        Record(
            actor,
            AuditAction.BookingUpgradeApproved,
            booking,
            null,
            $"Upgrade approved. Moved from {wasOn} to {booking.CourtName}, and {upgrade.BalanceDue:N2} was paid.");

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Upgrade {UpgradeId} approved at the desk. Booking {BookingId} moved to {BookableCourtId}.",
            upgradeId,
            booking.Id,
            target.Id);

        return DeskResult<DeskUpgrade>.Success(await OneUpgradeAsync(upgradeId, ct));
    }

    public async Task<DeskResult<DeskUpgrade>> DeclineUpgradeAsync(
        Guid userId,
        Guid upgradeId,
        string? reason,
        AuditActor actor,
        CancellationToken ct)
    {
        var upgrade = await ForDeskUpgradeAsync(userId, upgradeId, ct);

        if (upgrade is null)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeNotFound);
        }

        if (upgrade.Status != UpgradeStatus.AwaitingApproval)
        {
            return DeskResult<DeskUpgrade>.Fail(DeskFailure.UpgradeNotWaiting);
        }

        upgrade.Decline(userId, reason, timeProvider.GetUtcNow());

        Record(
            actor,
            AuditAction.BookingUpgradeDeclined,
            upgrade.Booking,
            reason,
            $"Upgrade to {upgrade.ToCourtName} was declined. The booking stays where it is.");

        await db.SaveChangesAsync(ct);

        // No letter yet, for the same reason a rejected booking gets none: a
        // refusal needs somewhere for the customer to answer from, and that is
        // the message thread, which is not built.
        logger.LogInformation(
            "Upgrade {UpgradeId} was declined at the desk. The customer has not been emailed.",
            upgradeId);

        return DeskResult<DeskUpgrade>.Success(await OneUpgradeAsync(upgradeId, ct));
    }

    /// <summary>
    /// The upgrade, tracked, and only if its booking sits at a venue this
    /// person works. One elsewhere answers the same as one that is not there.
    /// </summary>
    private async Task<BookingUpgradeRequest?> ForDeskUpgradeAsync(
        Guid userId,
        Guid upgradeId,
        CancellationToken ct)
    {
        var venueIds = VenueQuery(userId).Select(facility => facility.Id);

        return await db.BookingUpgradeRequests
            .Include(row => row.Slots)
            .Include(row => row.Booking)
            .ThenInclude(booking => booking.Slots)
            .SingleOrDefaultAsync(
                row => row.Id == upgradeId
                    && venueIds.Contains(row.Booking.BookableCourt.Court.FacilityId),
                ct);
    }

    /// <summary>
    /// Whether anybody else holds one of the hours being asked for.
    ///
    /// An upgrade holds its hours with a clock rather than a lock — the court
    /// stays on sale while the customer pays — so this is the last place a
    /// clash can be caught before two people are sent to the same floor.
    /// </summary>
    private async Task<bool> IsTakenAsync(
        Domain.Facilities.BookableCourt target,
        Guid exceptBooking,
        IReadOnlyCollection<BookingUpgradeSlot> wanted,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var dates = wanted.Select(slot => slot.Date).Distinct().ToArray();
        var live = BookingStatuses.Live;

        var held = await db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                slot.CourtId == target.CourtId
                && dates.Contains(slot.Date)
                && slot.BookingId != exceptBooking
                && live.Contains(slot.Booking.Status)
                // The same rule as Booking.HasLapsedAt, asked in SQL: an unpaid
                // hold that has run out with no receipt holds nothing.
                && (slot.Booking.Status != BookingStatus.PendingPayment
                    || slot.Booking.ReceiptUrl != null
                    || utcNow < slot.Booking.HoldsUntil))
            .Select(slot => new
            {
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.BookableCourt.CourtSportId,
                slot.BookableCourt.DivisionNumber
            })
            .ToListAsync(ct);

        return held
            .Where(slot => target.ClashesWith(slot.CourtSportId, slot.DivisionNumber))
            .Any(slot => wanted.Any(want =>
                want.Date == slot.Date && want.StartsAt < slot.EndsAt && slot.StartsAt < want.EndsAt));
    }

    /// <summary>
    /// The venue's own wall clock. A court is played at the time the floor is
    /// in, never at the time the server happens to keep.
    /// </summary>
    private async Task<DateTimeOffset> VenueNowAsync(
        Guid facilityId,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var timeZone = await db.Facilities
            .AsNoTracking()
            .Where(facility => facility.Id == facilityId)
            .Select(facility => facility.TimeZone)
            .SingleOrDefaultAsync(ct);

        return timeZone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(utcNow, zone)
            : utcNow;
    }

    private async Task<DeskUpgrade> OneUpgradeAsync(Guid upgradeId, CancellationToken ct)
    {
        var row = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Where(candidate => candidate.Id == upgradeId)
            .Select(candidate => new UpgradeRow(
                candidate,
                candidate.Booking,
                candidate.Booking.BookableCourt.Court.FacilityId,
                candidate.Booking.BookableCourt.Court.Facility.Name,
                candidate.Booking.BookableCourt.CourtSport.Sport.Name,
                candidate.Booking.BookableCourt.CourtSport.Sport.Key,
                db.Users
                    .Where(user => user.Id == candidate.RequestedByUserId)
                    .Select(user => new Person(user.FullName, user.Email, user.PhoneNumber))
                    .FirstOrDefault(),
                candidate.Booking.Slots.ToList(),
                candidate.Slots.ToList()))
            .SingleAsync(ct);

        return Upgrade(row);
    }

    private static DeskUpgrade Upgrade(UpgradeRow row)
    {
        var who = row.Customer ?? NobodyKnown;

        return new DeskUpgrade(
            row.Request.Id,
            row.Request.BookingId,
            row.FacilityId,
            row.FacilityName,
            who.FullName,
            who.Email,
            who.PhoneNumber,
            row.SportName,
            row.SportKey,
            row.Booking.CourtName,
            row.Request.ToBookableCourtId,
            row.Request.ToCourtName,
            row.Request.RentalNow,
            row.Request.RentalNew,
            row.Request.BalanceDue,
            row.Request.Status,
            row.Request.ReceiptUrl,
            row.Request.ReceiptUploadedAt,
            row.Request.CreatedAt,
            row.Request.SettledAt,
            row.Request.DeclineReason,
            [
                .. row.BookingSlots
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
            [
                .. row.WantedSlots
                    .OrderBy(slot => slot.Date)
                    .ThenBy(slot => slot.StartsAt)
                    .Select(slot => new BookedSlot(
                        slot.Date,
                        slot.StartsAt,
                        slot.EndsAt,
                        slot.RateKind.ToString(),
                        slot.Amount,
                        slot.PlatformFee))
            ]);
    }

    private sealed record UpgradeRow(
        BookingUpgradeRequest Request,
        Booking Booking,
        Guid FacilityId,
        string FacilityName,
        string SportName,
        string SportKey,
        Person? Customer,
        IReadOnlyCollection<BookingSlot> BookingSlots,
        IReadOnlyCollection<BookingUpgradeSlot> WantedSlots);

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

    public async Task<DeskResult<DeskSettings>> SettingsAsync(Guid userId, CancellationToken ct)
    {
        var owner = await OwnerForAsync(userId, ct);

        return owner is null
            ? DeskResult<DeskSettings>.Fail(DeskFailure.NotAttended)
            : DeskResult<DeskSettings>.Success(Settings(owner));
    }

    public async Task<DeskResult<DeskSettings>> UpdateSettingsAsync(
        Guid userId,
        UpdateDeskSettingsRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = await OwnerForAsync(userId, ct);

        if (owner is null)
        {
            return DeskResult<DeskSettings>.Fail(DeskFailure.NotAttended);
        }

        var now = timeProvider.GetUtcNow();
        var before = SettingsSnapshot(owner);

        // Both are clamped rather than refused. A dial is a dial: somebody
        // typing 600 minutes means "the longest you allow", not "fail".
        owner.SetPaymentHold(request.PartialBookingExpiryMinutes, now);
        owner.SetMoveLimit(request.MoveLimit, now);

        audit.RecordChange(
            actor,
            AuditAction.FacilityOwnerDeskSettingsUpdated,
            AuditEntityType.FacilityOwner,
            owner.Id,
            before,
            SettingsSnapshot(owner));

        await db.SaveChangesAsync(ct);

        return DeskResult<DeskSettings>.Success(Settings(owner));
    }

    private static DeskSettings Settings(Domain.Identity.FacilityOwner owner) => new(
        owner.PartialBookingExpiryMinutes,
        owner.MoveLimit,
        PaymentHold.MinimumMinutes,
        PaymentHold.MaximumMinutes,
        BookingMove.SmallestLimit,
        BookingMove.LargestLimit);

    private static Dictionary<string, string?> SettingsSnapshot(Domain.Identity.FacilityOwner owner) =>
        new()
        {
            ["partialBookingExpiryMinutes"] =
                owner.PartialBookingExpiryMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["moveLimit"] = owner.MoveLimit.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    /// <summary>
    /// The venue owner behind this desk, whether the person asking owns it or
    /// is an attendant on it. Both set the same dials: an attendant is the one
    /// standing there when a customer says half an hour is too long to wait.
    /// </summary>
    private async Task<Domain.Identity.FacilityOwner?> OwnerForAsync(Guid userId, CancellationToken ct)
    {
        var ownerId = await VenueQuery(userId)
            .Select(facility => facility.FacilityOwnerId)
            .FirstOrDefaultAsync(ct);

        return ownerId == Guid.Empty
            ? null
            : await db.FacilityOwners.FirstOrDefaultAsync(candidate => candidate.Id == ownerId, ct);
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
        Record(actor, action, booking, reason, null);

    /// <summary>
    /// Writes what the desk just did into the platform's trail.
    ///
    /// The description is written for the customer to read, because the same
    /// trail is what their own booking history reads back. Without one, an
    /// entry can only say the name of the action it was.
    /// </summary>
    private void Record(
        AuditActor actor,
        string action,
        Booking booking,
        string? reason,
        string? description) =>
        audit.RecordEvent(
            actor,
            action,
            AuditEntityType.Booking,
            booking.Id,
            new Dictionary<string, string?>
            {
                ["description"] = description,
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
