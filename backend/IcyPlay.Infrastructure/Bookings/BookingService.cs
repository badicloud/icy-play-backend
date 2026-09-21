using System.Data;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// Answers what is free, and takes it.
///
/// Both halves price the same way, through the same code, because a customer
/// shown one number and charged another has been lied to — so the grid a
/// visitor reads and the bill they agree to come out of one method.
/// </summary>
public sealed class BookingService(
    AppDbContext db,
    ICloudinaryAssetService assets,
    IBookingNotifier notifier,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<BookingService> logger) : IBookingService
{
    public async Task<BookingResult<AvailabilityDay>> AvailabilityAsync(
        Guid bookableCourtId,
        DateOnly date,
        CancellationToken ct)
    {
        var offering = await LoadAsync(bookableCourtId, date, ct);

        if (offering is null)
        {
            return BookingResult<AvailabilityDay>.Fail(BookingFailure.CourtNotFound);
        }

        var holiday = await IsHolidayAsync(date, ct);
        var taken = await TakenAsync(offering, [date], ct);

        return BookingResult<AvailabilityDay>.Success(Day(offering, date, holiday, taken));
    }

    public async Task<BookingResult<IReadOnlyCollection<DayOutlook>>> OutlookAsync(
        Guid bookableCourtId,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // Loaded against today, which is where the window starts; the offering
        // carries the hours and the closures, so every other date in it is
        // answered from the same read.
        var offering = await LoadAsync(bookableCourtId, DateOnly.FromDateTime(now.UtcDateTime), ct);

        if (offering is null)
        {
            return BookingResult<IReadOnlyCollection<DayOutlook>>.Fail(BookingFailure.CourtNotFound);
        }

        // The venue's today, not the server's: in Manila a UTC clock is eight
        // hours behind, and a window starting on the wrong day greys out a day
        // that is still on sale.
        var today = offering.Today(now);
        var dates = Enumerable
            .Range(0, BookingWindow.DaysAhead + 1)
            .Select(today.AddDays)
            .ToArray();

        var holidays = await db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.IsActive)
            .ToArrayAsync(ct);

        // One read for the whole window rather than one per day.
        var taken = await TakenAsync(offering, dates, ct);

        return BookingResult<IReadOnlyCollection<DayOutlook>>.Success(
        [
            .. dates.Select(date =>
            {
                var day = Day(offering, date, holidays.Any(holiday => holiday.Covers(date)), taken);

                return new DayOutlook(
                    date,
                    day.IsClosed,
                    day.IsHoliday,
                    day.IsUnderMaintenance,
                    day.Slots.Count(slot => slot.IsOpen),
                    day.Slots.Count);
            })
        ]);
    }

    public async Task<BookingResult<BookingDetail>> CreateAsync(
        CreateBookingRequest request,
        Guid customerUserId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dates = request.Slots.Select(slot => slot.Date).Distinct().OrderBy(date => date).ToArray();

        var shape = CheckShape(request.Kind, dates);

        if (shape != BookingFailure.None)
        {
            return BookingResult<BookingDetail>.Fail(shape);
        }

        var offering = await LoadAsync(request.BookableCourtId, dates[0], ct);

        if (offering is null)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.CourtNotFound);
        }

        // The venue's today, not the server's. Opening hours are wall-clock in
        // the facility's zone, and in Manila a UTC clock is eight hours behind:
        // reading "today" off the server would sell yesterday's evening for most
        // of the working day.
        var today = offering.Today(timeProvider.GetUtcNow());

        if (dates[0] < today)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.DateInThePast);
        }

        if (dates[^1] > today.AddDays(BookingWindow.DaysAhead))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.TooFarAhead);
        }

        if (offering.Pair.StandardHourlyRate is null)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.NotPriced);
        }

        // Serializable, because the check and the write have to be one moment.
        // Two customers reading "free" a millisecond apart and both writing is
        // exactly how one court gets sold twice.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        // Every date the booking spans, not only the ones being bought: a run
        // is allowed to pass over a day it cannot have, and whether it could
        // have had it is a question about that day.
        var spanned = Enumerable
            .Range(0, dates[^1].DayNumber - dates[0].DayNumber + 1)
            .Select(dates[0].AddDays)
            .ToArray();

        var holidays = new Dictionary<DateOnly, bool>();

        foreach (var date in spanned)
        {
            holidays[date] = await IsHolidayAsync(date, ct);
        }

        var taken = await TakenAsync(offering, spanned, ct);
        var window = spanned.ToDictionary(date => date, date => Day(offering, date, holidays[date], taken));
        var days = dates.ToDictionary(date => date, date => window[date]);

        if (request.Kind == BookingKind.MultiDay && !RunIsUnbroken(window, dates))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.DatesNotConsecutive);
        }

        var failure = CheckSlots(request, days);

        if (failure != BookingFailure.None)
        {
            return BookingResult<BookingDetail>.Fail(failure);
        }

        var takenAt = timeProvider.GetUtcNow();
        var booking = new Booking(
            offering.BookableCourt.Id,
            customerUserId,
            request.Kind,
            offering.CourtName,
            offering.FacilityName,
            offering.SportName,
            offering.PlatformHourlyRate,
            dates[0],
            dates[^1],
            offering.HoldMinutes,
            takenAt);

        db.Bookings.Add(booking);

        foreach (var wanted in request.Slots.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt))
        {
            var slot = days[wanted.Date].Slots.Single(candidate => candidate.StartsAt == wanted.StartsAt);

            db.BookingSlots.Add(new BookingSlot(
                booking.Id,
                offering.Court.Id,
                offering.BookableCourt.Id,
                wanted.Date,
                slot.StartsAt,
                slot.EndsAt,
                Enum.Parse<CourtRateKind>(slot.RateKind),
                slot.Rate!.Value,
                slot.PlatformFee,
                takenAt));
        }

        Record(
            customerUserId,
            AuditAction.BookingCreated,
            booking,
            $"Booked {booking.CourtName} for {Hours(booking)}.");

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Booking {BookingId} taken on {BookableCourtId} for {Hours} hours by {CustomerUserId}.",
            booking.Id,
            offering.BookableCourt.Id,
            request.Slots.Count,
            customerUserId);

        var detail = await GetAsync(booking.Id, customerUserId, ct);

        return detail;
    }

    public async Task<BookingResult<BookingDetail>> MoveAsync(
        Guid bookingId,
        Guid customerUserId,
        MoveBookingRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        // Another customer's booking answers the same as one that is not there.
        if (booking is null)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.CourtNotFound);
        }

        var limit = await MoveLimitAsync(booking, ct);

        if (booking.MoveCount >= limit)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.MoveLimitReached);
        }

        var quoted = await QuoteAsync(booking, request.ToBookableCourtId, request.Slots, ct);

        if (!quoted.Succeeded)
        {
            return BookingResult<BookingDetail>.Fail(quoted.Failure);
        }

        var quote = quoted.Value!;

        // Dearer hours are refused rather than absorbed.
        //
        // The move happens the moment it is asked for: the venue is not asked
        // to approve it and the customer is not sent to a checkout, so there is
        // no step left where money could change hands. Letting this through
        // would hand the customer better hours and hand the venue the bill,
        // without either of them being asked.
        //
        // Court rental on both sides. The platform fee is charged per hour
        // booked and a move buys no hours, so counting it would refuse a move
        // between two courts that cost exactly the same.
        if (quote.RentalNew > quote.RentalNow)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.MoveCostsMore);
        }

        // The same court at the same hours is not a move, and charging the
        // venue's limit for it would spend somebody's allowance on nothing.
        // Checked here rather than in the quote: the quote answers what a move
        // WOULD come to, and a screen asking that while the customer is still
        // choosing should not be told off for it.
        var before = booking.Slots
            .Select(slot => (slot.Date, slot.StartsAt))
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .ToArray();

        var after = quote.Kept
            .Concat(quote.Moved)
            .Select(slot => (slot.Date, slot.StartsAt))
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .ToArray();

        if (quote.ToBookableCourtId == booking.BookableCourtId && after.SequenceEqual(before))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.NothingWouldChange);
        }

        var utcNow = timeProvider.GetUtcNow();
        var wasOn = booking.CourtName;
        var wasFor = Hours(booking);

        // Nothing settled: a cheaper court is not a refund, which is the rule
        // the booking policy states and the move screen repeats.
        Apply(booking, quote, settled: 0m, countsAgainstTheLimit: true, utcNow);

        // Written after the move, so the entry describes where it landed, and
        // carrying where it came from — which the booking itself no longer
        // says, because a booking only ever knows where it is now.
        Record(
            customerUserId,
            AuditAction.BookingMoved,
            booking,
            wasOn == booking.CourtName
                ? $"Moved from {wasFor} to {Hours(booking)}."
                : $"Moved from {wasOn} ({wasFor}) to {booking.CourtName} ({Hours(booking)}).");

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Booking {BookingId} moved to {BookableCourtId}, move {MoveCount} of {Limit}.",
            booking.Id,
            request.ToBookableCourtId,
            booking.MoveCount,
            limit);

        // After the save, and best effort. A free move needs nobody's
        // permission and has already happened, so a mail provider being down
        // must not report it as failed — but the venue's diary has changed
        // without anybody at the venue touching it, and this is the only thing
        // that says so.
        await notifier.BookingMovedAsync(
            booking,
            new BookingMoveNotice(wasOn, wasFor, Hours(booking)),
            ct);

        return await GetAsync(bookingId, customerUserId, ct);
    }

    public async Task<BookingResult<MoveQuoteResponse>> QuoteMoveAsync(
        Guid bookingId,
        Guid customerUserId,
        Guid toBookableCourtId,
        IReadOnlyCollection<BookingSlotInput>? wanted,
        CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        if (booking is null)
        {
            return BookingResult<MoveQuoteResponse>.Fail(BookingFailure.CourtNotFound);
        }

        var quoted = await QuoteAsync(booking, toBookableCourtId, wanted, ct);

        return quoted.Succeeded
            ? BookingResult<MoveQuoteResponse>.Success(Quoted(booking, quoted.Value!))
            : BookingResult<MoveQuoteResponse>.Fail(quoted.Failure);
    }

    public async Task<BookingResult<UpgradeRequestResponse>> RequestUpgradeAsync(
        Guid bookingId,
        Guid customerUserId,
        MoveBookingRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        if (booking is null)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.CourtNotFound);
        }

        // The same dial a free move is counted against. An upgrade is still a
        // move, and paying for one must not be a way around the venue's limit.
        var limit = await MoveLimitAsync(booking, ct);

        if (booking.MoveCount >= limit)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.MoveLimitReached);
        }

        var utcNow = timeProvider.GetUtcNow();
        var open = await OpenUpgradeAsync(bookingId, utcNow, ct);

        // One at a time. Two open requests and the customer can be paying for
        // hours while the venue is approving different ones.
        if (open is not null)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.MoveAlreadyRequested);
        }

        var quoted = await QuoteAsync(booking, request.ToBookableCourtId, request.Slots, ct);

        if (!quoted.Succeeded)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(quoted.Failure);
        }

        var quote = quoted.Value!;

        // Nothing to pay is nothing to upgrade. That move is free and immediate,
        // and sending somebody to a checkout for nought pesos is a step whose
        // only effect is to make them wonder what they are being charged for.
        if (quote.RentalNew <= quote.RentalNow)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.NothingToUpgrade);
        }

        var upgrade = new BookingUpgradeRequest(
            booking.Id,
            quote.ToBookableCourtId,
            quote.ToCourtName,
            customerUserId,
            quote.RentalNow,
            quote.RentalNew,
            quote.HoldMinutes,
            utcNow);

        // Copied off the quote rather than pointing at it: these are hours the
        // booking does not hold yet, and may never hold. They carry the price
        // they were quoted at, because that is the figure being paid against.
        foreach (var slot in quote.Moved.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt))
        {
            upgrade.Slots.Add(new BookingUpgradeSlot(
                upgrade.Id,
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.RateKind,
                slot.Amount,
                slot.PlatformFee,
                utcNow));
        }

        db.BookingUpgradeRequests.Add(upgrade);

        Record(
            customerUserId,
            AuditAction.BookingUpgradeRequested,
            booking,
            $"Asked to upgrade to {upgrade.ToCourtName} ({Hours(upgrade)}) for {upgrade.BalanceDue:N2}, waiting to be paid.");

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Booking {BookingId} asked to upgrade to {BookableCourtId} for {BalanceDue}.",
            booking.Id,
            upgrade.ToBookableCourtId,
            upgrade.BalanceDue);

        return BookingResult<UpgradeRequestResponse>.Success(Upgraded(upgrade, utcNow));
    }

    public async Task<BookingResult<UpgradeRequestResponse?>> OpenUpgradeAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var owned = await db.Bookings
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        // Another customer's booking answers the same as one that is not there.
        if (!owned)
        {
            return BookingResult<UpgradeRequestResponse?>.Fail(BookingFailure.CourtNotFound);
        }

        var utcNow = timeProvider.GetUtcNow();
        var open = await OpenUpgradeAsync(bookingId, utcNow, ct);

        return BookingResult<UpgradeRequestResponse?>.Success(
            open is null ? null : Upgraded(open, utcNow));
    }

    public async Task<BookingResult<UpgradeRequestResponse>> AttachUpgradeReceiptAsync(
        Guid bookingId,
        Guid customerUserId,
        AttachReceiptRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var found = await FindOpenUpgradeAsync(bookingId, customerUserId, ct);

        if (!found.Succeeded)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(found.Failure);
        }

        var upgrade = found.Value!;
        var utcNow = timeProvider.GetUtcNow();

        // Waiting to be paid for, or already with the venue and having the
        // picture swapped for a better one. Anything else is settled.
        var waiting = upgrade.Status == UpgradeStatus.AwaitingPayment;

        if (!waiting && upgrade.Status != UpgradeStatus.AwaitingApproval)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.NotAwaitingPayment);
        }

        // Checked exactly as a booking's own receipt is. The browser reports
        // where it put the file, so the link is the customer's word for it: a
        // link off the platform's own Cloudinary account is one this venue
        // would be shown and nobody here could vouch for.
        if (!assets.IsTrustedSecureUrl(request.ReceiptUrl))
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.UntrustedReceiptUrl);
        }

        if (!IsImage(request.ReceiptUrl))
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.ReceiptNotAnImage);
        }

        // The court being upgraded to, not the one the booking is on. They are
        // at the same venue, so the answer is the same — but asking about the
        // court being paid for is the question that stays right if that ever
        // stops being true.
        if (!await CanBePaidAsync(upgrade.ToBookableCourtId, ct))
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.VenueCannotBePaid);
        }

        upgrade.AttachReceipt(request.ReceiptUrl, utcNow);

        var booking = await db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleAsync(candidate => candidate.Id == bookingId, ct);

        // Sending the receipt IS the submission, the same as it is on a
        // booking's own checkout. They were two steps, and the second one
        // contradicted the message above it: the page already said the venue
        // was checking, then asked you to send it.
        if (waiting)
        {
            upgrade.Submit(utcNow);

            Record(
                customerUserId,
                AuditAction.BookingUpgradePaymentSubmitted,
                booking,
                $"Sent {upgrade.BalanceDue:N2} to the venue for the upgrade to {upgrade.ToCourtName}, waiting for them to check it.");
        }
        else
        {
            // The venue already has one and is being handed another. Recorded,
            // because the desk must be able to see that what it is looking at
            // is not what it was first shown.
            Record(
                customerUserId,
                AuditAction.BookingUpgradePaymentSubmitted,
                booking,
                $"Sent a different receipt for the upgrade to {upgrade.ToCourtName}.");
        }

        await db.SaveChangesAsync(ct);

        if (waiting)
        {
            // After the save, and best effort: a mail provider having a bad
            // afternoon must not undo a submission the customer has been told
            // went through. The venue still sees it in their queue.
            //
            // Only on the first one. A replaced picture is not a second
            // payment and must not read as one.
            await notifier.UpgradeSubmittedAsync(upgrade, ct);
        }

        return BookingResult<UpgradeRequestResponse>.Success(Upgraded(upgrade, utcNow));
    }

    /// <summary>
    /// The customer's own open upgrade, tracked and ready to be changed.
    ///
    /// Both halves of paying for one start here, so the ownership check and the
    /// "is there one at all" check are written once rather than twice.
    /// </summary>
    private async Task<BookingResult<BookingUpgradeRequest>> FindOpenUpgradeAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var owned = await db.Bookings
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        if (!owned)
        {
            return BookingResult<BookingUpgradeRequest>.Fail(BookingFailure.CourtNotFound);
        }

        var open = await OpenUpgradeAsync(bookingId, timeProvider.GetUtcNow(), ct);

        // Nothing open covers both "never asked" and "the clock ran out while
        // they were in GCash". The second is the one that will actually happen,
        // and the message the customer sees says so.
        return open is null
            ? BookingResult<BookingUpgradeRequest>.Fail(BookingFailure.MoveRequestNotFound)
            : BookingResult<BookingUpgradeRequest>.Success(open);
    }

    /// <summary>
    /// The upgrade still holding hours on this booking, if any.
    ///
    /// An unpaid request that has run out of clock is let go here rather than
    /// by a job somewhere: the hours it was holding are free the moment it
    /// lapses, and the next thing to ask is the thing that should find out.
    /// </summary>
    private async Task<BookingUpgradeRequest?> OpenUpgradeAsync(
        Guid bookingId,
        DateTimeOffset utcNow,
        CancellationToken ct)
    {
        var candidates = await db.BookingUpgradeRequests
            .Include(candidate => candidate.Slots)
            .Where(candidate =>
                candidate.BookingId == bookingId
                && (candidate.Status == UpgradeStatus.AwaitingPayment
                    || candidate.Status == UpgradeStatus.AwaitingApproval))
            .ToListAsync(ct);

        var lapsed = candidates.Where(candidate => !candidate.HoldsTheCourtAt(utcNow)).ToArray();

        foreach (var expired in lapsed)
        {
            expired.Expire(utcNow);
        }

        if (lapsed.Length > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return candidates.Except(lapsed).OrderByDescending(candidate => candidate.CreatedAt).FirstOrDefault();
    }

    private static UpgradeRequestResponse Upgraded(BookingUpgradeRequest upgrade, DateTimeOffset utcNow) => new(
        upgrade.Id,
        upgrade.BookingId,
        upgrade.ToBookableCourtId,
        upgrade.ToCourtName,
        upgrade.RentalNow,
        upgrade.RentalNew,
        upgrade.BalanceDue,
        upgrade.Status,
        upgrade.HoldsUntil,
        // Read against the server's clock, because that is the clock the hold
        // was set by. A browser an hour out would otherwise send somebody away
        // from hours they still hold, or keep them paying for hours they lost.
        upgrade.Status == UpgradeStatus.AwaitingPayment
            && upgrade.ReceiptUrl is null
            && utcNow >= upgrade.HoldsUntil,
        upgrade.ReceiptUrl,
        upgrade.DeclineReason,
        [
            .. upgrade.Slots
                .OrderBy(slot => slot.Date)
                .ThenBy(slot => slot.StartsAt)
                .Select(slot => new UpgradeSlotResponse(
                    slot.Date,
                    slot.StartsAt,
                    slot.EndsAt,
                    slot.Amount))
        ]);

    /// <summary>The hours an upgrade is asking for, as a person would say them.</summary>
    private static string Hours(BookingUpgradeRequest upgrade)
    {
        var ordered = upgrade.Slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .ToArray();

        if (ordered.Length == 0)
        {
            return "no hours";
        }

        var first = ordered[0];
        var last = ordered[^1];

        return first.Date == last.Date
            ? $"{first.Date:d MMM}, {first.StartsAt:h:mm tt} to {last.EndsAt:h:mm tt}"
            : $"{first.Date:d MMM} to {last.Date:d MMM}";
    }

    /// <summary>
    /// Puts the booking on the new court: the hours that moved replace the ones
    /// that did not, and the hours already played keep their own court on each
    /// slot, so a session that changed courts half way through can still say
    /// which half was where.
    /// </summary>
    private void Apply(
        Booking booking,
        MoveQuote quote,
        decimal settled,
        bool countsAgainstTheLimit,
        DateTimeOffset now)
    {
        db.BookingSlots.RemoveRange(booking.Slots.Except(quote.Kept));

        booking.MoveTo(
            quote.ToBookableCourtId,
            quote.ToCourtName,
            quote.Kept,
            quote.Moved,
            countsAgainstTheLimit,
            now);

        db.BookingSlots.AddRange(quote.Moved);
        booking.Settle(settled, now);
    }

    private static MoveQuoteResponse Quoted(Booking booking, MoveQuote quote) => new(
        quote.ToBookableCourtId,
        quote.ToCourtName,
        quote.Kept.Count,
        quote.Moved.Count,
        quote.RentalNow,
        quote.RentalNew,
        Math.Max(0m, quote.RentalNew - quote.RentalNow),
        quote.HoldMinutes,
        quote.IsInPlay);

    /// <summary>
    /// What a booking would come to on another court, and what that leaves the
    /// customer owing.
    ///
    /// Hours already played do not move and are not re-priced: re-pricing an
    /// hour somebody has had is charging them for a court they were never on.
    /// Everything still to come is priced on the new court at that court's own
    /// rates, for the same dates and the same times.
    /// </summary>
    private async Task<BookingResult<MoveQuote>> QuoteAsync(
        Booking booking,
        Guid toBookableCourtId,
        IReadOnlyCollection<BookingSlotInput>? wanted,
        CancellationToken ct)
    {
        if (!BookingMove.IsMovable(booking.Status))
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.NotMovable);
        }

        var ordered = booking.Slots.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt).ToArray();

        if (ordered.Length == 0)
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.NotMovable);
        }

        // Loaded against the earliest date the booking would end up on, which
        // is what the venue's term has to cover. When the hours are moving to
        // another day that is not the date the booking is on now.
        var earliest = wanted is { Count: > 0 }
            ? wanted.Min(slot => slot.Date)
            : ordered[0].Date;

        var target = await LoadAsync(toBookableCourtId, earliest, ct);

        if (target is null)
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.CourtNotFound);
        }

        if (target.Pair.StandardHourlyRate is null)
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.NotPriced);
        }

        // The same sport, at the same venue.
        //
        // Every way of moving a booking comes through here — the customer's
        // request, the attendant's move, and the confirmation afterwards — so
        // this is the one place the rule has to hold. Without it a pickleball
        // booking could be sent to a badminton court in another building, and
        // the booking would keep saying "Pickleball" and the old venue's name
        // because those are recorded on it as they were sold.
        var from = await db.BookableCourts
            .AsNoTracking()
            .Where(candidate => candidate.Id == booking.BookableCourtId)
            .Select(candidate => new
            {
                candidate.CourtSport.SportId,
                candidate.Court.FacilityId
            })
            .SingleOrDefaultAsync(ct);

        if (from is null
            || from.SportId != target.Pair.SportId
            || from.FacilityId != target.Court.FacilityId)
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.NotTheSameOffering);
        }

        var utcNow = timeProvider.GetUtcNow();
        var venueNow = target.LocalNow(utcNow);

        // Played, and so staying put. An hour counts as played once its last
        // minute has gone on the venue's clock.
        var played = ordered
            .Where(slot => slot.Date.ToDateTime(slot.EndsAt) <= venueNow.DateTime)
            .ToArray();
        var toMove = ordered.Except(played).ToArray();

        if (toMove.Length == 0)
        {
            // Every hour has been played. There is nothing left to move, and a
            // booking that is over is a refund rather than a move.
            return BookingResult<MoveQuote>.Fail(BookingFailure.BookingFinished);
        }

        // Where the hours are going. Their own dates and times unless the
        // customer picked others, in which case there have to be exactly as
        // many: a move changes when and where a booking is, never how much of
        // it there is, and a screen that could add an hour by moving would be
        // a way of buying one without paying.
        var going = wanted is null
            ? [.. toMove.Select(slot => new BookingSlotInput(slot.Date, slot.StartsAt))]
            : wanted.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt).ToArray();

        if (going.Length != toMove.Length)
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.KindDoesNotMatchSlots);
        }

        var dates = going.Select(slot => slot.Date).Distinct().OrderBy(date => date).ToArray();

        var holidays = new Dictionary<DateOnly, bool>();

        foreach (var date in dates)
        {
            holidays[date] = await IsHolidayAsync(date, ct);
        }

        var taken = await TakenAsync(target, dates, ct, exceptBooking: booking.Id);
        var days = dates.ToDictionary(date => date, date => Day(target, date, holidays[date], taken));

        var priced = new List<BookingSlot>();

        foreach (var slot in going)
        {
            var offered = days[slot.Date].Slots
                .FirstOrDefault(candidate => candidate.StartsAt == slot.StartsAt);

            if (offered is null)
            {
                return BookingResult<MoveQuote>.Fail(BookingFailure.OutsideOpeningHours);
            }

            if (!offered.IsOpen)
            {
                return BookingResult<MoveQuote>.Fail(BookingFailure.SlotTaken);
            }

            priced.Add(new BookingSlot(
                booking.Id,
                target.Court.Id,
                target.BookableCourt.Id,
                slot.Date,
                offered.StartsAt,
                offered.EndsAt,
                Enum.Parse<CourtRateKind>(offered.RateKind),
                offered.Rate!.Value,
                offered.PlatformFee,
                utcNow));
        }

        // Court rental only, on both sides.
        //
        // The platform fee is charged per hour booked, and a move buys no
        // hours: the same number of them end up somewhere else. Counting it
        // would make a move between two identically priced courts look as
        // though it cost something.
        var rentalNow = booking.Slots.Sum(slot => slot.Amount);
        var rentalNew = played.Sum(slot => slot.Amount) + priced.Sum(slot => slot.Amount);

        return BookingResult<MoveQuote>.Success(new MoveQuote(
            target.BookableCourt.Id,
            target.CourtName,
            played,
            priced,
            rentalNow,
            rentalNew,
            target.HoldMinutes,
            // Has the first hour begun, on the venue's clock. A booking under
            // way can change court but not when it is; one that has not
            // started can change both.
            //
            // This asked whether the hours fell on the venue's today, which
            // refused a date to every booking later the same day — an eight
            // o'clock tonight is today and has not begun, and the server would
            // happily have moved it to tomorrow.
            ordered[0].Date.ToDateTime(ordered[0].StartsAt) <= venueNow.DateTime));
    }

    /// <summary>
    /// Writes what just happened into the platform's trail.
    ///
    /// The same trail the venue's desk writes to, keyed on the booking, so one
    /// booking has one account of itself. The description is written for the
    /// customer to read, because they are who reads it back.
    /// </summary>
    private void Record(Guid customerUserId, string action, Booking booking, string description) =>
        audit.RecordEvent(
            new AuditActor(customerUserId, UserRoleName.Customer),
            action,
            AuditEntityType.Booking,
            booking.Id,
            new Dictionary<string, string?>
            {
                ["description"] = description,
                ["court"] = booking.CourtName,
                ["total"] = booking.Total.ToString("0.00")
            });

    /// <summary>The booking's hours, as a person would say them.</summary>
    private static string Hours(Booking booking)
    {
        var ordered = booking.Slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .ToArray();

        if (ordered.Length == 0)
        {
            return "no hours";
        }

        var first = ordered[0];
        var last = ordered[^1];

        return first.Date == last.Date
            ? $"{first.Date:d MMM}, {first.StartsAt:h:mm tt} to {last.EndsAt:h:mm tt}"
            : $"{first.Date:d MMM} to {last.Date:d MMM}";
    }

    /// <summary>
    /// How many moves this venue allows a customer on one booking. Per venue,
    /// because it is their court being held while somebody makes up their mind.
    /// </summary>
    private async Task<int> MoveLimitAsync(Booking booking, CancellationToken ct) =>
        await db.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.Id == booking.BookableCourtId)
            .Select(unit => unit.Court.Facility.FacilityOwner.MoveLimit)
            .FirstOrDefaultAsync(ct) is var limit && limit > 0
            ? limit
            : BookingMove.DefaultLimit;

    public async Task<BookingResult<IReadOnlyCollection<BookingHistoryEntry>>> HistoryAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct)
    {
        // Another customer's booking answers the same as one that is not there,
        // so an id cannot be probed for whose it is.
        var booking = await db.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                row => row.Id == bookingId && row.CustomerUserId == customerUserId,
                ct);

        if (booking is null)
        {
            return BookingResult<IReadOnlyCollection<BookingHistoryEntry>>.Fail(
                BookingFailure.CourtNotFound);
        }

        // The reading is shared with the venue desk's own history. One
        // booking has one account of itself, and two readers of the same
        // events that disagreed would be worse than either being wrong.
        return BookingResult<IReadOnlyCollection<BookingHistoryEntry>>.Success(
            await BookingHistory.ReadAsync(db, booking, timeProvider.GetUtcNow(), ct));
    }


    public async Task<BookingResult<BookingDetail>> GetAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var row = await db.Bookings
            .AsNoTracking()
            .Include(candidate => candidate.Slots)
            .Where(candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId)
            .Select(candidate => new
            {
                Booking = candidate,
                SportKey = candidate.BookableCourt.CourtSport.Sport.Key,
                candidate.BookableCourt.Court.Facility.FacilityOwner.GcashNumber,
                candidate.BookableCourt.Court.Facility.FacilityOwner.GcashAccountName,
                candidate.BookableCourt.Court.Facility.FacilityOwner.GcashQrCodeUrl,
                candidate.BookableCourt.Court.Facility.ContactPhone,
                candidate.BookableCourt.Court.Facility.ContactEmail,
                Upgrade = db.BookingUpgradeRequests
                    .Where(request => request.BookingId == candidate.Id
                        && (request.Status == UpgradeStatus.AwaitingPayment
                            || request.Status == UpgradeStatus.AwaitingApproval))
                    .OrderByDescending(request => request.CreatedAt)
                    .Select(request => new { request.Status, request.ToCourtName })
                    .FirstOrDefault(),
                candidate.BookableCourt.Court.Facility.TimeZone,
                candidate.BookableCourt.Court.Facility.FacilityOwner.MoveLimit,
                candidate.BookableCourt.Court.FacilityId
            })
            .SingleOrDefaultAsync(ct);

        // Another customer's booking answers the same as one that does not
        // exist, so an id cannot be probed for whether it is somebody's.
        return row is null
            ? BookingResult<BookingDetail>.Fail(BookingFailure.CourtNotFound)
            : BookingResult<BookingDetail>.Success(Detail(
                row.Booking,
                row.SportKey,
                row.GcashNumber,
                row.GcashAccountName,
                row.GcashQrCodeUrl,
                row.ContactPhone,
                row.ContactEmail,
                row.Upgrade?.Status,
                row.Upgrade?.ToCourtName,
                row.TimeZone,
                row.MoveLimit,
                row.FacilityId));
    }

    public async Task<IReadOnlyCollection<BookingDetail>> ListForCustomerAsync(
        Guid customerUserId,
        CancellationToken ct)
    {
        var rows = await db.Bookings
            .AsNoTracking()
            .Include(booking => booking.Slots)
            .Where(booking => booking.CustomerUserId == customerUserId)
            .OrderByDescending(booking => booking.CreatedAt)
            .Select(booking => new
            {
                Booking = booking,
                SportKey = booking.BookableCourt.CourtSport.Sport.Key,
                booking.BookableCourt.Court.Facility.FacilityOwner.GcashNumber,
                booking.BookableCourt.Court.Facility.FacilityOwner.GcashAccountName,
                booking.BookableCourt.Court.Facility.FacilityOwner.GcashQrCodeUrl,
                booking.BookableCourt.Court.Facility.ContactPhone,
                booking.BookableCourt.Court.Facility.ContactEmail,
                Upgrade = db.BookingUpgradeRequests
                    .Where(request => request.BookingId == booking.Id
                        && (request.Status == UpgradeStatus.AwaitingPayment
                            || request.Status == UpgradeStatus.AwaitingApproval))
                    .OrderByDescending(request => request.CreatedAt)
                    .Select(request => new { request.Status, request.ToCourtName })
                    .FirstOrDefault(),
                booking.BookableCourt.Court.Facility.TimeZone,
                booking.BookableCourt.Court.Facility.FacilityOwner.MoveLimit,
                booking.BookableCourt.Court.FacilityId
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => Detail(
                row.Booking,
                row.SportKey,
                row.GcashNumber,
                row.GcashAccountName,
                row.GcashQrCodeUrl,
                row.ContactPhone,
                row.ContactEmail,
                row.Upgrade?.Status,
                row.Upgrade?.ToCourtName,
                row.TimeZone,
                row.MoveLimit,
                row.FacilityId))
        ];
    }

    public async Task<BookingResult<BookingDetail>> AttachReceiptAsync(
        Guid bookingId,
        Guid customerUserId,
        AttachReceiptRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var booking = await FindAsync(bookingId, customerUserId, ct);

        if (booking is null)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.CourtNotFound);
        }

        var now = timeProvider.GetUtcNow();

        // Waiting to be paid for, or already with the venue and having the
        // picture swapped for a better one. Anything else — confirmed,
        // rejected, cancelled — is settled, and a receipt changes nothing.
        var waiting = booking.Status == BookingStatus.PendingPayment;

        if (!waiting && booking.Status != BookingStatus.PendingVerification)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.NotAwaitingPayment);
        }

        if (booking.HasLapsedAt(now))
        {
            // The hours went back on sale while they were paying. Better to say
            // so than to take a receipt for a court somebody else now holds.
            return BookingResult<BookingDetail>.Fail(BookingFailure.HoldExpired);
        }

        if (!assets.IsTrustedSecureUrl(request.ReceiptUrl))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.UntrustedReceiptUrl);
        }

        if (!IsImage(request.ReceiptUrl))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.ReceiptNotAnImage);
        }

        if (!await CanBePaidAsync(booking.BookableCourtId, ct))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.VenueCannotBePaid);
        }

        booking.AttachReceipt(request.ReceiptUrl, now);

        // Sending the receipt IS the submission. They were two steps, and the
        // gap between them was a hole: attaching stops the hold's clock, so the
        // customer was told the venue was checking it while the booking sat in
        // PendingPayment — which is in neither of the desk's queues. The court
        // was held indefinitely and nobody was looking at it.
        if (waiting)
        {
            booking.SubmitForVerification(now);

            Record(
                customerUserId,
                AuditAction.BookingPaymentSubmitted,
                booking,
                "Payment sent to the venue to check.");
        }
        else
        {
            // The venue already has one and is being handed another. Recorded,
            // because the desk must be able to see that what it is looking at
            // is not what it was first shown.
            Record(
                customerUserId,
                AuditAction.BookingPaymentSubmitted,
                booking,
                "Sent a different receipt to the venue.");
        }

        await db.SaveChangesAsync(ct);

        if (waiting)
        {
            // After the save, and deliberately: a mail provider being down must
            // not undo a submission the customer has already been told went
            // through. Only on the first one — a replaced picture is not a
            // second payment and must not read as one.
            await notifier.PaymentSubmittedAsync(booking, ct);
        }

        return await GetAsync(bookingId, customerUserId, ct);
    }

    public async Task<BookingResult<bool>> CancelAsync(
        Guid bookingId,
        Guid customerUserId,
        string? reason,
        CancellationToken ct)
    {
        var booking = await db.Bookings
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        if (booking is null)
        {
            return BookingResult<bool>.Fail(BookingFailure.CourtNotFound);
        }

        if (!booking.HoldsTheCourtAt(timeProvider.GetUtcNow()))
        {
            // Already let go. Saying so beats a second cancellation that looks
            // like it did something.
            return BookingResult<bool>.Success(false);
        }

        booking.Cancel(reason, timeProvider.GetUtcNow());
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Booking {BookingId} cancelled by {CustomerUserId}.", bookingId, customerUserId);

        return BookingResult<bool>.Success(true);
    }

    /// <summary>
    /// A booking, for the customer who made it. Another customer's answers the
    /// same as one that does not exist, so an id cannot be probed for whether it
    /// belongs to somebody.
    /// </summary>
    private Task<Booking?> FindAsync(Guid bookingId, Guid customerUserId, CancellationToken ct) =>
        db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

    /// <summary>
    /// Whether a Cloudinary URL points at a picture.
    ///
    /// Reads the delivery URL rather than the file: Cloudinary puts the resource
    /// type in the path, so a video or a raw upload cannot masquerade as an
    /// image without being served from a different one. It is a check on the
    /// reference, not on the bytes — proving the bytes would mean asking
    /// Cloudinary about the asset, which is a round trip this does not need to
    /// stop somebody uploading a PDF.
    /// </summary>
    /// <summary>
    /// Whether this court's venue has any way of being paid at all.
    ///
    /// Asked before a receipt is taken, because a receipt is evidence of a
    /// payment — and a venue with neither a GCash number nor a QR code has no
    /// account for one to have been sent to. Without this the desk is handed a
    /// picture it cannot check against anything.
    /// </summary>
    private async Task<bool> CanBePaidAsync(Guid bookableCourtId, CancellationToken ct)
    {
        // The owner is fetched and the domain asked, rather than the same test
        // written again in LINQ. It is one rule — "has this venue any way of
        // being paid" — and a second copy of it is a second thing to keep in
        // step with the first.
        var owner = await db.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.Id == bookableCourtId)
            .Select(unit => unit.Court.Facility.FacilityOwner)
            .FirstOrDefaultAsync(ct);

        return owner?.CanTakePayment ?? false;
    }

    private static bool IsImage(string url)
    {
        if (!url.Contains("/image/upload/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = url.Split('?')[0];
        var extension = Path.GetExtension(path);

        return extension.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic";
    }

    // ---------------------------------------------------------------- loading

    /// <summary>
    /// The court as a customer can see it: active, in an active venue, whose
    /// owner is live on the date asked about. Anything else answers null, and
    /// the caller turns that into "no such court" — a court somebody cannot
    /// book is not usefully different from one that does not exist.
    /// </summary>
    private async Task<Offering?> LoadAsync(Guid bookableCourtId, DateOnly date, CancellationToken ct)
    {
        var unit = await db.BookableCourts
            .AsNoTracking()
            .Include(candidate => candidate.CourtSport).ThenInclude(pair => pair.Sport)
            .Include(candidate => candidate.Court).ThenInclude(court => court.Facility)
                .ThenInclude(facility => facility.FacilityOwner).ThenInclude(owner => owner.Contracts)
            .Include(candidate => candidate.Court).ThenInclude(court => court.OperatingHours)
            .Include(candidate => candidate.Court).ThenInclude(court => court.Facility)
                .ThenInclude(facility => facility.OperatingHours)
            .SingleOrDefaultAsync(candidate => candidate.Id == bookableCourtId, ct);

        if (unit is null || !unit.IsActive)
        {
            return null;
        }

        var court = unit.Court;
        var facility = court.Facility;
        var owner = facility.FacilityOwner;

        if (!court.IsActive || !facility.IsActive || !owner.IsActive || !unit.CourtSport.Sport.IsActive)
        {
            return null;
        }

        var contract = owner.Contracts.FirstOrDefault(candidate => candidate.Covers(date));

        if (contract is null)
        {
            return null;
        }

        var closed = await db.MaintenancePeriods
            .AsNoTracking()
            .Where(period =>
                period.LiftedAt == null &&
                (period.CourtId == court.Id || (period.CourtId == null && period.FacilityId == facility.Id)))
            .Select(period => new { period.StartsAt, period.EndsAt })
            .ToListAsync(ct);

        return new Offering(
            unit,
            court,
            unit.CourtSport,
            Court.DivisionName(
                court.Name,
                unit.CourtSport.Sport.Name,
                unit.DivisionNumber,
                unit.CourtSport.Divisions),
            facility.Name,
            unit.CourtSport.Sport.Name,
            facility.TimeZone,
            contract.PlatformHourlyRate,
            owner.PartialBookingExpiryMinutes,
            [.. closed.Select(period => (period.StartsAt, period.EndsAt))]);
    }

    private async Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct)
    {
        // The repeating ones cannot be matched on their stored date in SQL, so
        // the active rows are read and asked, the way the holiday console does.
        var holidays = await db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.IsActive)
            .ToArrayAsync(ct);

        return holidays.Any(holiday => holiday.Covers(date));
    }

    /// <summary>
    /// The hours already held on this floor across the dates asked about,
    /// narrowed to the ones that actually clash with this offering.
    ///
    /// Reads the whole floor rather than this bookable court, because that is
    /// the rule: basketball being played blocks all three pickleball courts,
    /// and only the parts of the same sport run side by side.
    /// </summary>
    private async Task<IReadOnlyCollection<(DateOnly Date, TimeOnly StartsAt, TimeOnly EndsAt)>> TakenAsync(
        Offering offering,
        IReadOnlyCollection<DateOnly> dates,
        CancellationToken ct,
        Guid? exceptBooking = null)
    {
        var live = BookingStatuses.Live;

        var now = timeProvider.GetUtcNow();

        var held = await db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                slot.CourtId == offering.Court.Id &&
                dates.Contains(slot.Date) &&
                // A booking being moved is not in its own way. Identified by
                // the booking and not by the hour: another court on the same
                // floor at the same time is somebody else's, and dropping it
                // for looking alike would move this booking on top of them.
                (exceptBooking == null || slot.BookingId != exceptBooking) &&
                live.Contains(slot.Booking.Status) &&
                // The same rule as Booking.HasLapsedAt, asked in SQL: an unpaid
                // hold that has run out with no receipt is not holding anything.
                (slot.Booking.Status != BookingStatus.PendingPayment ||
                    slot.Booking.ReceiptUrl != null ||
                    now < slot.Booking.HoldsUntil))
            .Select(slot => new Holding(
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.BookableCourt.CourtSportId,
                slot.BookableCourt.DivisionNumber))
            .ToListAsync(ct);

        return
        [
            .. held
                .Where(slot => offering.BookableCourt.ClashesWith(slot.CourtSportId, slot.DivisionNumber))
                .Select(slot => (slot.Date, slot.StartsAt, slot.EndsAt))
        ];
    }

    // ------------------------------------------------------------- the answer

    private AvailabilityDay Day(
        Offering offering,
        DateOnly date,
        bool isHoliday,
        IReadOnlyCollection<(DateOnly Date, TimeOnly StartsAt, TimeOnly EndsAt)> taken)
    {
        var court = offering.Court;
        var venueNow = offering.LocalNow(timeProvider.GetUtcNow());
        var hours = OpeningHours(offering, date.DayOfWeek);
        var underMaintenance = offering.ClosedFor(date);

        if (hours is not (TimeOnly opensAt, TimeOnly closesAt))
        {
            return new AvailabilityDay(
                offering.BookableCourt.Id,
                offering.CourtName,
                offering.FacilityName,
                offering.SportName,
                date,
                IsClosed: true,
                isHoliday,
                underMaintenance,
                court.SlotLengthMinutes,
                court.MinimumDurationMinutes,
                offering.PlatformHourlyRate,
                offering.Pair.StandardHourlyRate,
                offering.Pair.PeakHourlyRate,
                offering.Pair.WeekendRate,
                offering.Pair.HolidayRate,
                court.PeakStartsAt,
                court.PeakEndsAt,
                court.PeakOnWeekdays,
                court.PeakOnWeekends,
                []);
        }

        var slots = new List<AvailabilitySlot>();
        var length = TimeSpan.FromMinutes(court.SlotLengthMinutes);

        for (var start = opensAt; start.Add(length) <= closesAt; start = start.Add(length))
        {
            var end = start.Add(length);
            var price = offering.Pair.PriceAt(court, date, start, isHoliday);
            var held = taken.Any(slot => slot.Date == date && slot.StartsAt < end && start < slot.EndsAt);
            // An hour that has already begun cannot be sold. Nothing else was
            // stopping today's six in the morning being booked at three in the
            // afternoon.
            var gone = date < DateOnly.FromDateTime(venueNow.DateTime) ||
                (date == DateOnly.FromDateTime(venueNow.DateTime) &&
                    start <= TimeOnly.FromDateTime(venueNow.DateTime));

            slots.Add(new AvailabilitySlot(
                start,
                end,
                IsOpen: !held && !gone && !underMaintenance && price is not null,
                HasPassed: gone,
                (price?.Kind ?? CourtRateKind.Standard).ToString(),
                price is null ? null : PerSlot(price.Amount, court.SlotLengthMinutes),
                PerSlot(offering.PlatformHourlyRate, court.SlotLengthMinutes)));
        }

        return new AvailabilityDay(
            offering.BookableCourt.Id,
            offering.CourtName,
            offering.FacilityName,
            offering.SportName,
            date,
            IsClosed: false,
            isHoliday,
            underMaintenance,
            court.SlotLengthMinutes,
            court.MinimumDurationMinutes,
            offering.PlatformHourlyRate,
            offering.Pair.StandardHourlyRate,
            offering.Pair.PeakHourlyRate,
            offering.Pair.WeekendRate,
            offering.Pair.HolidayRate,
            court.PeakStartsAt,
            court.PeakEndsAt,
            court.PeakOnWeekdays,
            court.PeakOnWeekends,
            slots);
    }

    /// <summary>
    /// The court's own hours when it has opted out, otherwise the building's.
    /// Null on a day either says it is shut.
    /// </summary>
    private static (TimeOnly, TimeOnly)? OpeningHours(Offering offering, DayOfWeek day)
    {
        var court = offering.Court;

        if (!court.UsesFacilityHours)
        {
            var own = court.OperatingHours.FirstOrDefault(hour => hour.DayOfWeek == day);

            return own is { OpensAt: TimeOnly opens, ClosesAt: TimeOnly closes }
                ? (opens, closes)
                : null;
        }

        var shared = court.Facility.OperatingHours.FirstOrDefault(hour => hour.DayOfWeek == day);

        return shared is { OpensAt: TimeOnly facilityOpens, ClosesAt: TimeOnly facilityCloses }
            ? (facilityOpens, facilityCloses)
            : null;
    }

    /// <summary>
    /// An hourly rate, charged for a slot that may be shorter than an hour. A
    /// venue on half-hour slots charges half, which is what "per hour" means.
    /// </summary>
    private static decimal PerSlot(decimal hourlyRate, int slotLengthMinutes) =>
        Math.Round(hourlyRate * slotLengthMinutes / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// One hour somebody has a claim on, and which part of the floor that claim
    /// is against. Both the booked hours and the hours waiting to move in are
    /// read into this shape so the clash rule can be asked once of both.
    /// </summary>
    private sealed record Holding(
        DateOnly Date,
        TimeOnly StartsAt,
        TimeOnly EndsAt,
        Guid CourtSportId,
        int DivisionNumber);

    /// <summary>
    /// What a move would come to: the hours staying put, the hours priced on
    /// the new court, and the total of the two.
    /// </summary>
    private sealed record MoveQuote(
        Guid ToBookableCourtId,
        string ToCourtName,
        IReadOnlyCollection<BookingSlot> Kept,
        IReadOnlyCollection<BookingSlot> Moved,
        /// <summary>What the booking's hours come to now, in court rental alone.</summary>
        decimal RentalNow,
        /// <summary>What they would come to after the move, in court rental alone.</summary>
        decimal RentalNew,
        int HoldMinutes,
        /// <summary>
        /// Whether the booking has begun on the venue's clock, which is what
        /// decides whether the move screen offers dates.
        /// </summary>
        bool IsInPlay);

    // -------------------------------------------------------------- the rules

    /// <summary>
    /// Whether the count of dates matches the kind of booking claimed. Whether
    /// a run of them is unbroken is a separate question, and one this cannot
    /// answer: see <see cref="RunIsUnbroken"/>.
    /// </summary>
    private static BookingFailure CheckShape(string kind, IReadOnlyList<DateOnly> dates) => kind switch
    {
        BookingKind.Hourly => dates.Count == 1
            ? BookingFailure.None
            : BookingFailure.KindDoesNotMatchSlots,
        BookingKind.WholeDay => dates.Count == 1
            ? BookingFailure.None
            : BookingFailure.KindDoesNotMatchSlots,
        BookingKind.MultiDay when dates.Count < 2 => BookingFailure.KindDoesNotMatchSlots,
        BookingKind.MultiDay => BookingFailure.None,
        _ => BookingFailure.KindDoesNotMatchSlots
    };

    /// <summary>
    /// Whether a run of days is one run: everything between its ends, less the
    /// days that had nothing left on them.
    ///
    /// A day inside the run may be passed over only when there was no hour of
    /// it to be had — the venue is shut, the court is closed for work, or every
    /// hour is already somebody else's. A venue closing one day a week could
    /// otherwise never sell a run longer than six days.
    ///
    /// A day with hours still free may not be skipped. Leaving one out is not a
    /// run with a hole in it; it is two bookings wearing one name, priced and
    /// moved as though they were one.
    ///
    /// Asked inside the transaction, because whether an hour was free is only
    /// true at a moment.
    /// </summary>
    private static bool RunIsUnbroken(
        IReadOnlyDictionary<DateOnly, AvailabilityDay> window,
        IReadOnlyList<DateOnly> dates)
    {
        for (var i = 1; i < dates.Count; i++)
        {
            for (var skipped = dates[i - 1].AddDays(1); skipped < dates[i]; skipped = skipped.AddDays(1))
            {
                if (window[skipped].Slots.Any(slot => slot.IsOpen))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Whether every hour asked for is actually on sale, and whether the set of
    /// them is a booking this venue makes.
    /// </summary>
    private static BookingFailure CheckSlots(
        CreateBookingRequest request,
        IReadOnlyDictionary<DateOnly, AvailabilityDay> days)
    {
        foreach (var day in days.Values)
        {
            if (day.IsUnderMaintenance)
            {
                return BookingFailure.UnderMaintenance;
            }

            if (day.IsClosed)
            {
                return BookingFailure.OutsideOpeningHours;
            }
        }

        // Asked about in this order on purpose, because the answer is advice: a
        // customer told "that hour has gone, pick again" when they asked for a
        // whole day has been pointed at a door that is not there.
        foreach (var wanted in request.Slots)
        {
            var slot = days[wanted.Date].Slots
                .FirstOrDefault(candidate => candidate.StartsAt == wanted.StartsAt);

            if (slot is null)
            {
                return BookingFailure.OutsideOpeningHours;
            }

            if (slot.Rate is null)
            {
                return BookingFailure.NotPriced;
            }
        }

        // A day sold open to close is every hour the court is open that day,
        // and a run is a row of those days. One hour gone and there is no whole
        // day left to sell -- and "book by the hour instead" is something the
        // customer can act on.
        //
        // A run used to take each day for whatever was still free on it, so a
        // single booked hour on the Wednesday cost nobody the rest of the week.
        // The trouble was what it sold: a "week" that quietly missed an
        // afternoon, priced as though it had not. A run is now whole days or it
        // is not a run, and a week with a hole in it is two bookings that say
        // what they are.
        if (request.Kind is BookingKind.WholeDay or BookingKind.MultiDay)
        {
            foreach (var day in days.Values)
            {
                var wantedOnDay = request.Slots.Count(slot => slot.Date == day.Date);

                if (wantedOnDay != day.Slots.Count || day.Slots.Any(slot => !slot.IsOpen))
                {
                    return BookingFailure.DayNotWhollyAvailable;
                }
            }
        }

        foreach (var wanted in request.Slots)
        {
            var slot = days[wanted.Date].Slots.Single(candidate => candidate.StartsAt == wanted.StartsAt);

            if (!slot.IsOpen)
            {
                return BookingFailure.SlotTaken;
            }
        }

        var first = days.Values.First();
        var minutes = request.Slots.Count * first.SlotLengthMinutes;

        return minutes < first.MinimumDurationMinutes
            ? BookingFailure.BelowMinimumDuration
            : BookingFailure.None;
    }

    private BookingDetail Detail(
        Booking booking,
        string sportKey,
        string? gcashNumber,
        string? gcashAccountName,
        string? gcashQrCodeUrl,
        string? contactPhone,
        string? contactEmail,
        string? upgradeStatus,
        string? upgradeToCourtName,
        string timeZone,
        int moveLimit,
        Guid facilityId) => new(
        booking.Id,
        booking.BookableCourtId,
        booking.Status.ToString(),
        booking.Kind,
        booking.CourtName,
        facilityId,
        booking.FacilityName,
        booking.SportName,
        sportKey,
        booking.StartDate,
        booking.EndDate,
        booking.BookedHours,
        booking.RentalTotal,
        booking.PlatformFeeTotal,
        booking.Total,
        booking.HoldsUntil,
        booking.HasLapsedAt(timeProvider.GetUtcNow()),
        booking.ReceiptUrl,
        gcashNumber,
        gcashAccountName,
        gcashQrCodeUrl,
        contactPhone,
        contactEmail,
        booking.CancellationReason,
        upgradeStatus,
        upgradeToCourtName,
        [
            .. booking.Slots
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
        Math.Max(0, moveLimit - booking.MoveCount),
        moveLimit,
        CanBeMoved(booking, timeZone, moveLimit),
        HasStarted(booking, timeZone),
        booking.CreatedAt);

    /// <summary>
    /// Whether the first hour has begun, on the venue's clock.
    ///
    /// A booking in play can still change court but not change when it is, so
    /// this is what the move screen reads to decide whether to offer dates at
    /// all. The venue's clock rather than the server's: in Manila they are
    /// eight hours apart, and the answer would be wrong for a third of the day.
    /// </summary>
    private bool HasStarted(Booking booking, string timeZone)
    {
        var first = booking.Slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .FirstOrDefault();

        if (first is null)
        {
            return false;
        }

        var venueNow = Offering.LocalNowIn(timeZone, timeProvider.GetUtcNow());

        return first.Date.ToDateTime(first.StartsAt) <= venueNow.DateTime;
    }

    /// <summary>
    /// Whether this booking could be moved if somebody asked right now.
    ///
    /// The same rules the move itself applies, asked ahead of time so the page
    /// can offer the button or explain why it cannot. Which courts are free is
    /// not among them: that is a question about those courts, and this one is
    /// about this booking.
    /// </summary>
    private bool CanBeMoved(Booking booking, string timeZone, int moveLimit)
    {
        if (!BookingMove.IsMovable(booking.Status))
        {
            return false;
        }

        if (booking.MoveCount >= moveLimit)
        {
            return false;
        }

        var last = booking.Slots.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt).LastOrDefault();

        if (last is null)
        {
            return false;
        }

        // While it is still being played is exactly when a move is worth most:
        // a court that fails at two o'clock has an afternoon left in it. Once
        // the last hour has gone there is nothing left to move.
        var venueNow = Offering.LocalNowIn(timeZone, timeProvider.GetUtcNow());

        return last.Date.ToDateTime(last.EndsAt) > venueNow.DateTime;
    }

    /// <summary>Everything one court needs to answer both questions, read once.</summary>
    private sealed record Offering(
        BookableCourt BookableCourt,
        Court Court,
        CourtSport Pair,
        string CourtName,
        string FacilityName,
        string SportName,
        string TimeZone,
        decimal PlatformHourlyRate,
        int HoldMinutes,
        IReadOnlyCollection<(DateTimeOffset StartsAt, DateTimeOffset? EndsAt)> Closures)
    {
        /// <summary>
        /// The moment, on the venue's wall clock. Opening hours, peak windows
        /// and holidays are all written in that zone, so "has this hour gone"
        /// has to be asked there too.
        ///
        /// An unrecognised zone falls back to UTC rather than throwing: a court
        /// that cannot be looked at is worse than one whose cut-off is off by
        /// the offset. That fallback is a last resort and not a safety net —
        /// it is silent, and in Manila it is eight hours wrong. What keeps a
        /// venue out of it is <see cref="TimeZoneRules"/>, which refuses a zone
        /// the platform cannot read at the moment somebody types it.
        /// </summary>
        public DateTimeOffset LocalNow(DateTimeOffset utcNow) => LocalNowIn(TimeZone, utcNow);

        /// <inheritdoc cref="LocalNow" />
        public static DateTimeOffset LocalNowIn(string timeZone, DateTimeOffset utcNow) =>
            TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
                ? TimeZoneInfo.ConvertTime(utcNow, zone)
                : utcNow;

        public DateOnly Today(DateTimeOffset utcNow) =>
            DateOnly.FromDateTime(LocalNow(utcNow).DateTime);

        /// <summary>
        /// Whether a closure touches this date at all. A court shut for the
        /// afternoon is shut for the day as far as booking it goes: there is no
        /// partial maintenance in the model, and inventing one here would put a
        /// second rule where the console has one.
        /// </summary>
        public bool ClosedFor(DateOnly date)
        {
            var dayStart = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var dayEnd = dayStart.AddDays(1);

            return Closures.Any(closure =>
                closure.StartsAt < dayEnd && (closure.EndsAt is null || dayStart < closure.EndsAt));
        }
    }
}
