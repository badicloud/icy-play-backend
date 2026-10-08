using System.Data;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.OpenPlays;
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
        // The venue's own window, and exactly that many days: the booking
        // page's strip is drawn from this, so a day the venue does not sell yet
        // is never shown to be picked.
        var today = offering.Today(now);
        var dates = Enumerable
            .Range(0, BookingWindow.ClampDays(offering.WindowDays))
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

        if (dates[^1] > BookingWindow.LastDay(today, offering.WindowDays))
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

        if (request.Kind == BookingKind.MultiDay && !SkipsOnlyWhatItCouldNotHave(window, dates))
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
            takenAt,
            offering.PaymentMode);

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

        var rules = await MoveRulesAsync(booking, ct);
        var closed = Gate(booking, rules);

        if (closed != BookingFailure.None)
        {
            return BookingResult<BookingDetail>.Fail(closed);
        }

        var utcNow = timeProvider.GetUtcNow();

        // One at a time, free or paid for. Two open and the desk could approve
        // one while the customer is still waiting on the other.
        if (await OpenUpgradeAsync(bookingId, utcNow, ct) is not null)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.MoveAlreadyRequested);
        }

        var quoted = await QuoteAsync(booking, request.ToBookableCourtId, request.Slots, ct);

        if (!quoted.Succeeded)
        {
            return BookingResult<BookingDetail>.Fail(quoted.Failure);
        }

        var quote = quoted.Value!;

        // Dearer hours are refused here rather than absorbed.
        //
        // Nothing on this road collects money: the venue is asked to agree to
        // the move, not to check a payment. Letting a dearer court through
        // would hand the customer better hours and the venue the bill. Those
        // are an upgrade, which is paid for before the venue is asked.
        //
        // Court rental on both sides. The platform fee is charged per hour
        // booked and a move buys no hours, so counting it would refuse a move
        // between two courts that cost exactly the same.
        if (quote.RentalNew > quote.RentalNow)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.MoveCostsMore);
        }

        // The same court at the same hours is not a move, and putting it in
        // front of the desk would ask somebody to approve nothing. Checked
        // here rather than in the quote: the quote answers what a move WOULD
        // come to, and a screen asking that while the customer is still
        // choosing should not be told off for it.
        if (NothingWouldChange(booking, quote))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.NothingWouldChange);
        }

        // Asked last, after every rule about the move itself: somebody whose
        // booking cannot move should be told that, not asked why they want to.
        var why = CheckReason(request);

        if (why != BookingFailure.None)
        {
            return BookingResult<BookingDetail>.Fail(why);
        }

        // Asked for, not done. The booking stays exactly where it is until
        // somebody at the desk says yes; the hours asked for are held in the
        // meantime so nobody else is sold them while the desk decides. Nothing
        // is counted against the venue's limit until it is approved — a move
        // the venue turned down is not one the customer has had.
        var move = Request(booking, quote, customerUserId, request, utcNow);

        db.BookingUpgradeRequests.Add(move);

        Record(
            customerUserId,
            AuditAction.BookingMoveRequested,
            booking,
            (move.ToCourtName == booking.CourtName
                ? $"Asked to move from {Hours(booking)} to {Hours(move)}, waiting for the venue."
                : $"Asked to move from {booking.CourtName} ({Hours(booking)}) to {move.ToCourtName} ({Hours(move)}), waiting for the venue.")
            + Because(request.Reason!, request.ReasonNote));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Booking {BookingId} asked to move to {BookableCourtId}, waiting for the venue.",
            booking.Id,
            request.ToBookableCourtId);

        // After the save, and best effort: a mail provider having a bad
        // afternoon must not report a request as failed that the desk can
        // already see in its queue.
        await notifier.MoveRequestedAsync(move, ct);

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

        var closed = Gate(booking, await MoveRulesAsync(booking, ct));

        if (closed != BookingFailure.None)
        {
            return BookingResult<MoveQuoteResponse>.Fail(closed);
        }

        var quoted = await QuoteAsync(booking, toBookableCourtId, wanted, ct);

        return quoted.Succeeded
            ? BookingResult<MoveQuoteResponse>.Success(Quoted(booking, quoted.Value!))
            : BookingResult<MoveQuoteResponse>.Fail(quoted.Failure);
    }

    public async Task<BookingResult<MoveWindow>> MoveWindowAsync(
        Guid bookingId,
        Guid customerUserId,
        DateOnly date,
        CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(candidate => candidate.Slots)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == bookingId && candidate.CustomerUserId == customerUserId,
                ct);

        if (booking is null)
        {
            return BookingResult<MoveWindow>.Fail(BookingFailure.CourtNotFound);
        }

        // Only an hourly booking picks hours. A day taken open to close has no
        // hours to choose — it is every hour there is — so a grid would be
        // offering a choice that does not exist.
        if (booking.Kind != BookingKind.Hourly)
        {
            return BookingResult<MoveWindow>.Fail(BookingFailure.KindDoesNotMatchSlots);
        }

        var closed = Gate(booking, await MoveRulesAsync(booking, ct));

        if (closed != BookingFailure.None)
        {
            return BookingResult<MoveWindow>.Fail(closed);
        }

        // Loaded against the booking's own court, which is not the court the
        // hours will end up on: none has been chosen yet. What it is being
        // asked for is the building — its clock, its opening hours, and the
        // length its hours are cut to.
        var offering = await LoadAsync(booking.BookableCourtId, date, ct);

        if (offering is null)
        {
            return BookingResult<MoveWindow>.Fail(BookingFailure.CourtNotFound);
        }

        var venueNow = offering.LocalNow(timeProvider.GetUtcNow());
        var today = DateOnly.FromDateTime(venueNow.DateTime);

        if (date < today)
        {
            return BookingResult<MoveWindow>.Fail(BookingFailure.DateInThePast);
        }

        if (date > BookingWindow.LastDay(today, offering.WindowDays))
        {
            return BookingResult<MoveWindow>.Fail(BookingFailure.TooFarAhead);
        }

        // How many hours have to be picked: the ones still ahead of the
        // booking, not the ones it has. An hour that has begun is being played
        // on the court it was sold on and does not travel.
        var needed = booking.Slots.Count(slot => !slot.HasBegunAt(venueNow.DateTime));

        if (needed == 0)
        {
            return BookingResult<MoveWindow>.Fail(BookingFailure.BookingFinished);
        }

        var length = offering.Court.SlotLengthMinutes;
        var isHoliday = await IsHolidayAsync(date, ct);

        if (BuildingHours(offering, date.DayOfWeek) is not (TimeOnly opensAt, TimeOnly closesAt))
        {
            return BookingResult<MoveWindow>.Success(
                new MoveWindow(date, IsClosed: true, isHoliday, length, needed, []));
        }

        var slots = new List<MoveWindowSlot>();
        var step = TimeSpan.FromMinutes(length);

        for (var start = opensAt; start.Add(step) <= closesAt; start = start.Add(step))
        {
            // The same cut-off the hour grid uses: an hour that has begun
            // cannot be moved onto, because it cannot be sold.
            var gone = date < today
                || (date == today && start <= TimeOnly.FromDateTime(venueNow.DateTime));

            slots.Add(new MoveWindowSlot(start, start.Add(step), gone));
        }

        return BookingResult<MoveWindow>.Success(
            new MoveWindow(date, IsClosed: false, isHoliday, length, needed, slots));
    }

    public async Task<BookingResult<MoveOptions>> MoveOptionsAsync(
        Guid bookingId,
        Guid customerUserId,
        MoveOptionsRequest request,
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
            return BookingResult<MoveOptions>.Fail(BookingFailure.CourtNotFound);
        }

        // Asked ahead of the search rather than left to it. Every court would
        // fail these for the same reason, and a search that comes back empty
        // says "nowhere to go" when the truth is "this booking cannot move".
        var closed = Gate(booking, await MoveRulesAsync(booking, ct));

        if (closed != BookingFailure.None)
        {
            return BookingResult<MoveOptions>.Fail(closed);
        }

        var from = await db.BookableCourts
            .AsNoTracking()
            .Where(candidate => candidate.Id == booking.BookableCourtId)
            .Select(candidate => new
            {
                candidate.CourtSport.SportId,
                candidate.Court.FacilityId,
                candidate.Court.Facility.TimeZone
            })
            .SingleOrDefaultAsync(ct);

        if (from is null)
        {
            return BookingResult<MoveOptions>.Fail(BookingFailure.CourtNotFound);
        }

        if (HasBegunByTheDay(booking, from.TimeZone))
        {
            return BookingResult<MoveOptions>.Fail(BookingFailure.DayBookingInPlay);
        }

        var venueNow = Offering.LocalNowIn(from.TimeZone, timeProvider.GetUtcNow());

        var moving = booking.Slots
            .Where(slot => !slot.HasBegunAt(venueNow.DateTime))
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .ToArray();

        if (moving.Length == 0)
        {
            return BookingResult<MoveOptions>.Fail(BookingFailure.BookingFinished);
        }

        // A booking sold by the day is searched by date; an hourly one by the
        // hours themselves. The difference is that a day's hours are not a
        // choice — they are whatever the court is open for — so they cannot be
        // named until a court is, and each candidate has to be asked about its
        // own day rather than about one list of hours.
        var byTheDay = booking.Kind is BookingKind.WholeDay or BookingKind.MultiDay;

        // How many days this booking is, which is how many it has to land on.
        // Counted from the booking rather than from its hours: a run of three
        // days is three days wherever it goes, however long each turns out to
        // be.
        var days = booking.Slots.Select(slot => slot.Date).Distinct().Count();

        // The dates sent have to be as many as the booking has, and each one
        // once. Checked here rather than left to the search: a miscount fails
        // every court in turn and comes back as an empty list, which reads as
        // a full venue rather than as a bad request.
        var wantedDates = byTheDay
            ? (request.Dates ?? []).Distinct().OrderBy(date => date).ToArray()
            : [];

        if (byTheDay && wantedDates.Length != days)
        {
            return BookingResult<MoveOptions>.Fail(BookingFailure.KindDoesNotMatchSlots);
        }

        // The hours sent have to be as many as are moving. Left to the search,
        // a miscount fails every court in turn and comes back as an empty list
        // — which reads as a full venue rather than as a bad request.
        if (!byTheDay && request.Slots is { Count: > 0 } asked && asked.Count != moving.Length)
        {
            return BookingResult<MoveOptions>.Fail(BookingFailure.KindDoesNotMatchSlots);
        }


        var candidates = await db.BookableCourts
            .AsNoTracking()
            .Where(candidate =>
                candidate.IsActive
                && candidate.Court.IsActive
                && candidate.CourtSport.SportId == from.SportId
                && candidate.Court.FacilityId == from.FacilityId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.CourtSport.StandardHourlyRate,
                SportName = candidate.CourtSport.Sport.Name
            })
            .ToListAsync(ct);

        var offers = new List<MoveOption>();

        foreach (var candidate in candidates)
        {
            var wanted = byTheDay
                ? await WholeDaysAsync(candidate.Id, booking.Id, wantedDates, ct)
                : request.Slots;

            // A court that cannot take the whole of those days is not an
            // option, and there is nothing to quote it for.
            if (byTheDay && wanted is null)
            {
                continue;
            }

            var quoted = await QuoteAsync(booking, candidate.Id, wanted, ct);

            // Shut that day, closed for work, already spoken for, never priced
            // — every reason a court cannot take this booking arrives as a
            // refused quote, and a refused quote is why it is not on the list.
            // The rules live in one place and the search reads them rather
            // than restating them.
            if (!quoted.Succeeded)
            {
                continue;
            }

            var quote = quoted.Value!;

            // Its own court at its own hours. That is not a move — the move
            // itself refuses it — so a card for it could only ever end in a
            // refusal.
            if (candidate.Id == booking.BookableCourtId && NothingWouldChange(booking, quote))
            {
                continue;
            }

            offers.Add(new MoveOption(
                quote.ToBookableCourtId,
                quote.ToCourtName,
                candidate.SportName,
                candidate.StandardHourlyRate,
                candidate.Id == booking.BookableCourtId,
                quote.MovingRentalNow,
                quote.MovingRentalNew,
                Math.Max(0m, quote.RentalNew - quote.RentalNow),
                [
                    .. quote.Moved
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
                quote.HoldMinutes));
        }

        return BookingResult<MoveOptions>.Success(new MoveOptions(
            // Some hour of it has been played, so the booking is under way.
            // The same thing the quote says, worked out here without needing a
            // court to be chosen first.
            moving.Length < booking.Slots.Count,
            booking.Slots.Count - moving.Length,
            moving.Length,
            [
                .. moving.Select(slot => new BookedSlot(
                    slot.Date,
                    slot.StartsAt,
                    slot.EndsAt,
                    slot.RateKind.ToString(),
                    slot.Amount,
                    slot.PlatformFee))
            ],
            // Cheapest first, so the free moves lead and the ones that want
            // paying for follow. A list ordered by court name puts a bill at
            // the top of the screen for no reason the reader can see.
            [.. offers.OrderBy(offer => offer.BalanceDue).ThenBy(offer => offer.CourtName)]));
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

        // The same rules a free move answers to. An upgrade is still a move,
        // and paying for one must not be a way round the venue's limit or its
        // notice.
        var closed = Gate(booking, await MoveRulesAsync(booking, ct));

        if (closed != BookingFailure.None)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(closed);
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

        // Nothing to pay is nothing to upgrade. That move is free and goes
        // straight to the venue, and sending somebody to a checkout for nought
        // pesos is a step whose only effect is to make them wonder what they
        // are being charged for.
        if (quote.MovingRentalNew <= quote.MovingRentalNow)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.NothingToUpgrade);
        }

        var why = CheckReason(request);

        if (why != BookingFailure.None)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(why);
        }

        // The hours that are moving, not the whole booking.
        //
        // The difference between the two pairs is the hours already played,
        // which sit on both sides and cancel — so the balance due is the same
        // figure whichever is stored, and for a booking that has not started
        // they ARE the same figure, because nothing has been played. What
        // changes is what the checkout can put on the page. A customer moving
        // the last hour of a long session is being asked for the difference on
        // that hour, and a receipt that opens with the total of a session
        // mostly behind them is a receipt for something else.
        var upgrade = Request(booking, quote, customerUserId, request, utcNow);

        db.BookingUpgradeRequests.Add(upgrade);

        Record(
            customerUserId,
            AuditAction.BookingUpgradeRequested,
            booking,
            $"Asked to upgrade to {upgrade.ToCourtName} ({Hours(upgrade)}) for {upgrade.BalanceDue:N2}, waiting to be paid."
            + Because(request.Reason!, request.ReasonNote));

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Booking {BookingId} asked to upgrade to {BookableCourtId} for {BalanceDue}.",
            booking.Id,
            upgrade.ToBookableCourtId,
            upgrade.BalanceDue);

        return BookingResult<UpgradeRequestResponse>.Success(Upgraded(upgrade, utcNow));
    }

    /// <summary>
    /// A move, asked for and not yet answered: the hours it wants, priced as
    /// they were quoted.
    ///
    /// The same record for a free move and an upgrade. What tells them apart
    /// is what is owed, and the record works that out for itself.
    /// </summary>
    private static BookingUpgradeRequest Request(
        Booking booking,
        MoveQuote quote,
        Guid customerUserId,
        MoveBookingRequest request,
        DateTimeOffset utcNow)
    {
        var move = new BookingUpgradeRequest(
            booking.Id,
            quote.ToBookableCourtId,
            quote.ToCourtName,
            customerUserId,
            quote.MovingRentalNow,
            quote.MovingRentalNew,
            quote.HoldMinutes,
            request.Reason!,
            request.ReasonNote,
            utcNow,
            quote.PaymentMode);

        // Copied off the quote rather than pointing at it: these are hours the
        // booking does not hold yet, and may never hold. They carry the price
        // they were quoted at, because that is the figure being paid against.
        foreach (var slot in quote.Moved.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt))
        {
            move.Slots.Add(new BookingUpgradeSlot(
                move.Id,
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.RateKind,
                slot.Amount,
                slot.PlatformFee,
                utcNow));
        }

        return move;
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

        // Paid through the gateway, which carries the move through by itself.
        if (upgrade.IsPaidDirect)
        {
            return BookingResult<UpgradeRequestResponse>.Fail(BookingFailure.PaidOnline);
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
        ],
        upgrade.PaymentChannel);

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

    private static MoveQuoteResponse Quoted(Booking booking, MoveQuote quote) => new(
        quote.ToBookableCourtId,
        quote.ToCourtName,
        quote.Kept.Count,
        quote.Moved.Count,
        quote.RentalNow,
        quote.RentalNew,
        quote.MovingRentalNow,
        quote.MovingRentalNew,
        [
            .. quote.Moved
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
        Math.Max(0m, quote.RentalNew - quote.RentalNow),
        quote.HoldMinutes,
        quote.IsInPlay,
        quote.PaymentMode);

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

        // A booking sold by the day, once that day has begun, does not move.
        //
        // Enforced here rather than at each of the three doors — the move, the
        // quote and the upgrade all come through this method — so there is one
        // place the rule lives and no way round it. An hourly booking under
        // way is untouched by it: the whole hours ahead of it are exactly what
        // a move is for.
        if (HasBegunByTheDay(booking, target.TimeZone))
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.DayBookingInPlay);
        }

        // Played, and so staying put. An hour counts as played once it has
        // BEGUN, not once it has finished.
        //
        // The hour running as somebody presses Move is half spent on a court
        // they are standing on. Carrying it to another floor would sell them
        // the whole of it again somewhere they can only have the rest of it,
        // and charge the new court's rate for minutes already played on the
        // old one. So the hour in progress stays where it is, at what it cost,
        // and the move takes the whole hours still ahead of it: a booking of
        // one to four, moved at ten past two, moves the three o'clock.
        //
        // It also makes the quote answerable at all. Availability marks an
        // hour that has begun as gone — correct, because such an hour cannot
        // be SOLD — so an hour in progress in this list came back `!IsOpen`
        // and the whole move was refused as "one of those hours has just been
        // taken". Leaving it behind means every hour that reaches the pricing
        // below is one that has not started, which is exactly what
        // availability is willing to talk about.
        //
        // Nothing changes for a booking that has not begun: none of its hours
        // have started, so none of them are held back and the move is the one
        // it always was.
        var played = ordered
            .Where(slot => slot.HasBegunAt(venueNow.DateTime))
            .ToArray();
        var toMove = ordered.Except(played).ToArray();

        if (toMove.Length == 0)
        {
            // Every hour has been played. There is nothing left to move, and a
            // booking that is over is a refund rather than a move.
            return BookingResult<MoveQuote>.Fail(BookingFailure.BookingFinished);
        }

        // Once it has started, what is left of it can change court and only
        // court. The hours are being played as the request is read, and
        // letting them wander to another day would turn a floodlight failing
        // into a way of putting off the rest of a session — hours the venue
        // has held all this time and can no longer sell.
        if (played.Length > 0
            && wanted is not null
            && !wanted
                .Select(slot => (slot.Date, slot.StartsAt))
                .OrderBy(slot => slot)
                .SequenceEqual(toMove.Select(slot => (slot.Date, slot.StartsAt)).OrderBy(slot => slot)))
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.CourtOnlyOnceStarted);
        }

        // Where the hours are going: their own dates and times unless the
        // customer picked others. How many of them there may be is the rule
        // below.
        var going = wanted is null
            ? [.. toMove.Select(slot => new BookingSlotInput(slot.Date, slot.StartsAt))]
            : wanted.OrderBy(slot => slot.Date).ThenBy(slot => slot.StartsAt).ToArray();

        // As many hours as are moving, on an hourly booking: a move changes
        // when and where a booking is, never how much of it there is, and a
        // screen that could add an hour by moving would be a way of buying one
        // without paying.
        //
        // A booking sold by the day is a different thing, and the rule would
        // be wrong on it. Its hours were never a number anybody chose — they
        // are whatever the court is open for — and a Tuesday is under no
        // obligation to be as long as the Saturday it replaces. Held to an
        // equal count, a whole-day booking could only ever move to a day of
        // exactly the same length, which on most rate cards means it could not
        // move at all.
        //
        // What keeps that honest is the price rather than the count. A longer
        // or dearer day comes out as a balance due, and a move that costs more
        // does not go through: it becomes an upgrade the customer is asked to
        // pay for and the venue is asked to accept. Nobody is handed hours
        // they have not paid for, which is the thing the count was protecting.
        // Not onto a date the venue does not sell yet. A date the booking
        // already holds is let through: a venue that shortens its window must
        // not strand the bookings it has already taken further out.
        var lastDay = BookingWindow.LastDay(DateOnly.FromDateTime(venueNow.DateTime), target.WindowDays);

        if (going.Any(slot => slot.Date > lastDay && booking.Slots.All(held => held.Date != slot.Date)))
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.TooFarAhead);
        }

        if (booking.Kind == BookingKind.Hourly && going.Length != toMove.Length)
        {
            return BookingResult<MoveQuote>.Fail(BookingFailure.KindDoesNotMatchSlots);
        }

        // A run of days stays a run of the same length wherever it goes. The
        // hours inside it may differ; the number of days may not, because that
        // is what was bought.
        if (booking.Kind != BookingKind.Hourly
            && going.Select(slot => slot.Date).Distinct().Count()
                != toMove.Select(slot => slot.Date).Distinct().Count())
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

        // The same comparison, narrowed to the hours actually going anywhere.
        //
        // The two totals above cover the whole booking, hours already played
        // included — and because those appear on both sides they cancel, so
        // the balance due is identical either way. What they cannot do is be
        // shown to anybody: telling somebody moving their last hour that they
        // are "paying 1,000 now" names a figure for a session mostly behind
        // them, and invites them to work out which part of it is still in
        // question. These name only the part that is.
        var movingRentalNow = toMove.Sum(slot => slot.Amount);
        var movingRentalNew = priced.Sum(slot => slot.Amount);

        return BookingResult<MoveQuote>.Success(new MoveQuote(
            target.BookableCourt.Id,
            target.CourtName,
            played,
            priced,
            rentalNow,
            rentalNew,
            movingRentalNow,
            movingRentalNew,
            target.HoldMinutes,
            // Has the first hour begun, on the venue's clock. A booking under
            // way can change court but not when it is; one that has not
            // started can change both.
            //
            // This asked whether the hours fell on the venue's today, which
            // refused a date to every booking later the same day — an eight
            // o'clock tonight is today and has not begun, and the server would
            // happily have moved it to tomorrow.
            ordered[0].Date.ToDateTime(ordered[0].StartsAt) <= venueNow.DateTime,
            target.PaymentMode));
    }

    /// <summary>
    /// Whether the customer said why they are moving, in a way that can be
    /// counted: a reason from the list, a few words when it is Other, and not a
    /// letter.
    /// </summary>
    private static BookingFailure CheckReason(MoveBookingRequest request)
    {
        if (!MoveReason.IsSupported(request.Reason))
        {
            return BookingFailure.MoveReasonRequired;
        }

        var note = request.ReasonNote?.Trim();

        if (request.Reason == MoveReason.Other && string.IsNullOrEmpty(note))
        {
            return BookingFailure.MoveReasonNoteRequired;
        }

        return note is { Length: > MoveReason.NoteLimit }
            ? BookingFailure.MoveReasonNoteTooLong
            : BookingFailure.None;
    }

    /// <summary>
    /// The reason, as the history reads it — which the desk reads too, so a
    /// diary that changed without anybody at the venue touching it says why.
    /// </summary>
    internal static string Because(string reason, string? note) =>
        string.IsNullOrWhiteSpace(note)
            ? $" Reason: {MoveReason.Label(reason)}."
            : $" Reason: {MoveReason.Label(reason)} — {note.Trim()}";

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
    /// How much moving this venue puts up with: how many moves on one booking,
    /// and how many days before it starts they close. Per venue, because it is
    /// their court being held while somebody makes up their mind.
    /// </summary>
    private async Task<MoveRules> MoveRulesAsync(Booking booking, CancellationToken ct)
    {
        var row = await db.BookableCourts
            .AsNoTracking()
            .Where(unit => unit.Id == booking.BookableCourtId)
            .Select(unit => new
            {
                unit.Court.Facility.FacilityOwner.MoveLimit,
                unit.Court.Facility.FacilityOwner.MoveNoticeDays,
                unit.Court.Facility.TimeZone
            })
            .FirstOrDefaultAsync(ct);

        return new MoveRules(
            row is { MoveLimit: > 0 } ? row.MoveLimit : BookingMove.DefaultLimit,
            row is { MoveNoticeDays: > 0 } ? row.MoveNoticeDays : BookingMove.DefaultNoticeDays,
            row?.TimeZone ?? Domain.Facilities.Facility.DefaultTimeZone);
    }

    private sealed record MoveRules(int Limit, int NoticeDays, string TimeZone);

    /// <summary>
    /// Whether this booking may be moved at all right now, before anybody asks
    /// where to: confirmed, moves left, and not inside the venue's notice.
    ///
    /// Asked at every door — the options, the grid, the quote, the move and
    /// the upgrade — so none of them is a way round it. Which court is free is
    /// not among these: that is a question about the court.
    /// </summary>
    private BookingFailure Gate(Booking booking, MoveRules rules)
    {
        if (!BookingMove.IsMovable(booking.Status))
        {
            return BookingFailure.NotMovable;
        }

        if (booking.MoveCount >= rules.Limit)
        {
            return BookingFailure.MoveLimitReached;
        }

        return IsInsideNotice(booking, rules.TimeZone, rules.NoticeDays)
            ? BookingFailure.TooLateToMove
            : BookingFailure.None;
    }

    /// <summary>
    /// Whether the booking has not started and its first hour is closer than
    /// the venue's notice, on the venue's clock.
    /// </summary>
    private bool IsInsideNotice(Booking booking, string timeZone, int noticeDays)
    {
        var first = booking.Slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .FirstOrDefault();

        return first is not null
            && BookingMove.IsInsideNotice(
                first.Date.ToDateTime(first.StartsAt),
                Offering.LocalNowIn(timeZone, timeProvider.GetUtcNow()).DateTime,
                noticeDays);
    }

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
                    .Select(request => new { request.Status, request.ToCourtName, request.BalanceDue })
                    .FirstOrDefault(),
                candidate.BookableCourt.Court.Facility.TimeZone,
                candidate.BookableCourt.Court.Facility.FacilityOwner.MoveLimit,
                candidate.BookableCourt.Court.Facility.FacilityOwner.MoveNoticeDays,
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
                row.Upgrade?.BalanceDue,
                row.TimeZone,
                row.MoveLimit,
                row.MoveNoticeDays,
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
                    .Select(request => new { request.Status, request.ToCourtName, request.BalanceDue })
                    .FirstOrDefault(),
                booking.BookableCourt.Court.Facility.TimeZone,
                booking.BookableCourt.Court.Facility.FacilityOwner.MoveLimit,
                booking.BookableCourt.Court.Facility.FacilityOwner.MoveNoticeDays,
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
                row.Upgrade?.BalanceDue,
                row.TimeZone,
                row.MoveLimit,
                row.MoveNoticeDays,
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

        // Paid through the gateway, which confirms it by itself. A receipt as
        // well would put a booking in front of the desk that may already be
        // paid, and a customer who paid both ways has paid twice.
        if (booking.IsPaidDirect)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.PaidOnline);
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

    private static bool IsImage(string url) => ReceiptLinks.IsImage(url);

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
            // Paying through the gateway means leaving the site and coming
            // back, so a direct term holds for longer than a receipt does.
            contract.TakesDirectPayment ? contract.OnlineHoldMinutes : owner.PartialBookingExpiryMinutes,
            owner.BookingWindowDays > 0 ? owner.BookingWindowDays : BookingWindow.DefaultDays,
            [.. closed.Select(period => (period.StartsAt, period.EndsAt))],
            contract.PaymentMode);
    }

    /// <summary>
    /// The building's hours for a day of the week.
    ///
    /// What the hour picker offers before a court has been chosen, which is a
    /// step the move screen now has: a date, then hours, then the courts that
    /// can take them. No court has been named at that point, so the hours
    /// cannot be any court's.
    ///
    /// It falls back to the court's own when the facility keeps none for that
    /// day. A venue whose courts all set their own hours would otherwise be
    /// shown as shut every day of the week, and a window of nothing is a
    /// harder thing to explain than a window that is slightly too generous —
    /// an hour offered here that no court can take simply leaves the court
    /// list empty, which is a true answer.
    /// </summary>
    private static (TimeOnly, TimeOnly)? BuildingHours(Offering offering, DayOfWeek day)
    {
        var shared = offering.Court.Facility.OperatingHours
            .FirstOrDefault(hour => hour.DayOfWeek == day);

        return shared is { OpensAt: TimeOnly opens, ClosesAt: TimeOnly closes }
            ? (opens, closes)
            : OpeningHours(offering, day);
    }

    /// <summary>
    /// Whether a booking sold by the day has begun, on the venue's clock.
    ///
    /// An hourly booking under way still has whole hours ahead of it, and
    /// carrying those to another court is the most useful thing a move does: a
    /// floodlight fails at two and the afternoon is saved. A day taken open to
    /// close has no such remainder to offer. Moving it at noon would leave a
    /// customer with a morning on one court and an afternoon on another, which
    /// is not the thing they bought — so the day it is on is the day it stays
    /// on, and the button that offers otherwise is not shown.
    ///
    /// False for an hourly booking whatever the clock says. This is a question
    /// about days, and asking it of an hourly booking would take away the move
    /// that matters most.
    /// </summary>
    private bool HasBegunByTheDay(Booking booking, string timeZone)
    {
        if (booking.Kind is not (BookingKind.WholeDay or BookingKind.MultiDay))
        {
            return false;
        }

        var venueNow = Offering.LocalNowIn(timeZone, timeProvider.GetUtcNow());

        var first = booking.Slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .FirstOrDefault();

        return first is not null && first.Date.ToDateTime(first.StartsAt) <= venueNow.DateTime;
    }

    /// <summary>
    /// Whether a move would leave the booking exactly where it is: the same
    /// court, at the same hours.
    ///
    /// Said in one place because two ask it. The move refuses it — letting it
    /// through rewrote a booking with what it already had and charged the
    /// venue's limit for the privilege — and the court search leaves it off
    /// the list, because a card whose only outcome is that refusal is a card
    /// that should not be offered.
    /// </summary>
    private static bool NothingWouldChange(Booking booking, MoveQuote quote)
    {
        if (quote.ToBookableCourtId != booking.BookableCourtId)
        {
            return false;
        }

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

        return after.SequenceEqual(before);
    }

    /// <summary>
    /// Every hour of the given dates on one court, or null when that court
    /// cannot take the whole of any one of them.
    ///
    /// What a booking sold by the day is searched with. Its hours are not a
    /// choice anybody made — they are whatever the court is open for — so they
    /// can only be named once a court is, which is why each candidate is asked
    /// about its own days rather than about one list of hours.
    ///
    /// The dates arrive named rather than as a start and a length, because the
    /// picker lets each be chosen and unchosen on its own. They need not run
    /// back to back: what has to hold is that there are as many as the booking
    /// has, which is settled before this is called.
    ///
    /// Null rather than a short list when a day cannot be had whole. A day
    /// sold open to close with an hour missing from it is not the thing that
    /// was sold, and handing back the hours that are left would quietly turn a
    /// whole day into most of one.
    /// </summary>
    private async Task<IReadOnlyCollection<BookingSlotInput>?> WholeDaysAsync(
        Guid bookableCourtId,
        Guid exceptBooking,
        IReadOnlyCollection<DateOnly> dates,
        CancellationToken ct)
    {
        if (dates.Count == 0)
        {
            return null;
        }

        var offering = await LoadAsync(bookableCourtId, dates.Min(), ct);

        if (offering is null)
        {
            return null;
        }

        // Not into the past, and not further ahead than the platform takes
        // bookings. The same window the booking page offers, asked here
        // because a move is a booking made again — and asked of every date,
        // because they are picked one at a time and need not sit together.
        var today = offering.Today(timeProvider.GetUtcNow());
        var horizon = BookingWindow.LastDay(today, offering.WindowDays);

        if (dates.Any(date => date < today || date > horizon))
        {
            return null;
        }

        var taken = await TakenAsync(offering, dates, ct, exceptBooking);
        var built = new List<BookingSlotInput>();

        foreach (var date in dates.OrderBy(date => date))
        {
            var day = Day(offering, date, await IsHolidayAsync(date, ct), taken);

            // Shut, closed for work, or with an hour already gone. All three
            // are the same answer here: this court cannot have that day.
            if (!day.CanBeHiredWhole)
            {
                return null;
            }

            built.AddRange(day.Slots.Select(slot => new BookingSlotInput(date, slot.StartsAt)));
        }

        return built;
    }

    /// <summary>
    /// The active holidays, read once for the life of this request.
    ///
    /// Held because the court search asks the same question over and over: one
    /// holiday check per candidate court per date, which on a venue with eight
    /// courts and a run of three days is two dozen reads of one small table to
    /// learn one thing that cannot have changed between them. Holidays are set
    /// at a console, not during a request, so the first answer is the right
    /// one for all of them.
    ///
    /// Safe because the service is scoped: this lives as long as the request
    /// does and is never shared with another.
    /// </summary>
    private IReadOnlyCollection<Holiday>? activeHolidays;

    private async Task<bool> IsHolidayAsync(DateOnly date, CancellationToken ct)
    {
        // The repeating ones cannot be matched on their stored date in SQL, so
        // the active rows are read and asked, the way the holiday console does.
        activeHolidays ??= await db.Holidays
            .AsNoTracking()
            .Where(holiday => holiday.IsActive)
            .ToArrayAsync(ct);

        return activeHolidays.Any(holiday => holiday.Covers(date));
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

        // The hours a move is asking for, while the venue decides. The desk has
        // been asked to give those hours to somebody, and selling them to
        // somebody else in the meantime leaves it approving a clash. Only once
        // it is with the desk: an upgrade still being paid for holds nothing,
        // as it never has — the court stays on sale while the customer is in
        // GCash, and the desk checks again when it approves.
        var asked = await db.BookingUpgradeSlots
            .AsNoTracking()
            .Where(slot =>
                slot.Request.ToBookableCourt.CourtId == offering.Court.Id &&
                dates.Contains(slot.Date) &&
                (exceptBooking == null || slot.Request.BookingId != exceptBooking) &&
                slot.Request.Status == UpgradeStatus.AwaitingApproval)
            .Select(slot => new Holding(
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.Request.ToBookableCourt.CourtSportId,
                slot.Request.ToBookableCourt.DivisionNumber))
            .ToListAsync(ct);

        held.AddRange(asked);

        // The hours a published open play holds. Its players have been told
        // the court is theirs, so it is not for sale to anybody else.
        var openPlays = await OpenPlayHolds.OnCourtAsync(db, offering.Court.Id, dates, ct);

        held.AddRange(openPlays.Select(hold => new Holding(
            hold.Date,
            hold.StartsAt,
            hold.EndsAt,
            hold.CourtSportId,
            hold.DivisionNumber)));

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
    internal static decimal PerSlot(decimal hourlyRate, int slotLengthMinutes) =>
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
        /// <summary>What the hours actually moving cost on the court they are leaving.</summary>
        decimal MovingRentalNow,
        /// <summary>And what those same hours come to on the court they are going to.</summary>
        decimal MovingRentalNew,
        int HoldMinutes,
        /// <summary>
        /// Whether the booking has begun on the venue's clock, which is what
        /// decides whether the move screen offers dates.
        /// </summary>
        bool IsInPlay,
        /// <summary>How the venue's term says the difference is paid, if there is one.</summary>
        string PaymentMode);

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
    /// <summary>
    /// Whether a run passes over only the days it could not have had.
    ///
    /// A run is a stretch of the calendar, and a day inside it that cannot be
    /// sold whole is passed over — not booked, not charged, and not quietly
    /// swallowed either: the customer is shown which days they are getting and
    /// pays for those. Somebody wanting the Thursday and the Saturday with the
    /// Friday already gone gets one booking for two days instead of two
    /// bookings with two holds, either of which they can lose.
    ///
    /// What it still refuses is reaching over a day that WAS free to book. That
    /// is a set of days rather than a run, which is a different thing to sell
    /// and a different thing to price, and nothing here offers it.
    ///
    /// The test is the day's own <see cref="AvailabilityDay.CanBeHiredWhole"/>,
    /// which is the same question the picker asks before it greys a day out. It
    /// used to be "has nothing open at all", which was stricter: a day with one
    /// hour gone was unbookable AND uncrossable, so a run simply stopped there.
    /// </summary>
    private static bool SkipsOnlyWhatItCouldNotHave(
        IReadOnlyDictionary<DateOnly, AvailabilityDay> window,
        IReadOnlyList<DateOnly> dates)
    {
        for (var i = 1; i < dates.Count; i++)
        {
            for (var skipped = dates[i - 1].AddDays(1); skipped < dates[i]; skipped = skipped.AddDays(1))
            {
                if (window[skipped].CanBeHiredWhole)
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
        decimal? upgradeBalanceDue,
        string timeZone,
        int moveLimit,
        int moveNoticeDays,
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
        upgradeBalanceDue,
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
        moveNoticeDays,
        // A move already waiting on the venue has to be answered before
        // another can be asked for.
        upgradeStatus is null && CanBeMoved(booking, timeZone, moveLimit, moveNoticeDays),
        BookingMove.IsMovable(booking.Status) && IsInsideNotice(booking, timeZone, moveNoticeDays),
        HasStarted(booking, timeZone),
        IsPlayingNow(booking, timeZone),
        booking.CreatedAt,
        booking.PaymentChannel);

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
    /// Whether one of the booked hours is running at this moment.
    ///
    /// The rule itself is <see cref="Booking.IsPlayingAt"/>, on the booking,
    /// where it can be read and tested without a database. All this adds is the
    /// clock — and specifically the VENUE's clock, which is the only one the
    /// slots mean anything against.
    ///
    /// <see cref="HasStarted"/> cannot stand in for this: it is true from the
    /// first hour onwards and never goes back to false, so by that reading a
    /// booking played last year is still in play now.
    /// </summary>
    private bool IsPlayingNow(Booking booking, string timeZone) =>
        booking.IsPlayingAt(Offering.LocalNowIn(timeZone, timeProvider.GetUtcNow()).DateTime);

    /// <summary>
    /// Whether this booking could be moved if somebody asked right now.
    ///
    /// The same rules the move itself applies, asked ahead of time so the page
    /// can offer the button or explain why it cannot. Which courts are free is
    /// not among them: that is a question about those courts, and this one is
    /// about this booking.
    /// </summary>
    private bool CanBeMoved(Booking booking, string timeZone, int moveLimit, int moveNoticeDays)
    {
        if (!BookingMove.IsMovable(booking.Status))
        {
            return false;
        }

        if (booking.MoveCount >= moveLimit)
        {
            return false;
        }

        // A day sold open to close, once it has begun, stays where it is.
        // There is no useful remainder to carry: moving it at noon leaves a
        // customer with a morning on one court and an afternoon on another,
        // which is not the thing they bought. Said here as well as in the
        // move itself, because this is what decides whether the button is
        // offered at all — and a button that only ever leads to a refusal is
        // worse than no button.
        if (HasBegunByTheDay(booking, timeZone))
        {
            return false;
        }

        // Too close to the start for the hours it gives back to be sold again.
        if (IsInsideNotice(booking, timeZone, moveNoticeDays))
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
        /// <summary>How many days ahead this venue sells, today included.</summary>
        int WindowDays,
        IReadOnlyCollection<(DateTimeOffset StartsAt, DateTimeOffset? EndsAt)> Closures,
        /// <summary>How the term in force says this venue is paid. See <see cref="Domain.Payments.PaymentMode"/>.</summary>
        string PaymentMode)
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
            VenueClock.LocalNowIn(timeZone, utcNow);

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
