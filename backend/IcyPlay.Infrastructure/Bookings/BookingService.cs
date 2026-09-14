using System.Data;
using IcyPlay.Application.Bookings;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Facilities;
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

        var holidays = new Dictionary<DateOnly, bool>();

        foreach (var date in dates)
        {
            holidays[date] = await IsHolidayAsync(date, ct);
        }

        var taken = await TakenAsync(offering, dates, ct);
        var days = dates.ToDictionary(date => date, date => Day(offering, date, holidays[date], taken));

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
                wanted.Date,
                slot.StartsAt,
                slot.EndsAt,
                Enum.Parse<CourtRateKind>(slot.RateKind),
                slot.Rate!.Value,
                slot.PlatformFee,
                takenAt));
        }

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
                candidate.BookableCourt.Court.Facility.FacilityOwner.GcashQrCodeUrl
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
                row.GcashQrCodeUrl));
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
                booking.BookableCourt.Court.Facility.FacilityOwner.GcashQrCodeUrl
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => Detail(
                row.Booking,
                row.SportKey,
                row.GcashNumber,
                row.GcashAccountName,
                row.GcashQrCodeUrl))
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

        if (booking.Status != BookingStatus.PendingPayment)
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

        booking.AttachReceipt(request.ReceiptUrl, now);
        await db.SaveChangesAsync(ct);

        return await GetAsync(bookingId, customerUserId, ct);
    }

    public async Task<BookingResult<BookingDetail>> SubmitForVerificationAsync(
        Guid bookingId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var booking = await FindAsync(bookingId, customerUserId, ct);

        if (booking is null)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.CourtNotFound);
        }

        var now = timeProvider.GetUtcNow();

        if (booking.Status != BookingStatus.PendingPayment)
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.NotAwaitingPayment);
        }

        if (booking.HasLapsedAt(now))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.HoldExpired);
        }

        if (string.IsNullOrWhiteSpace(booking.ReceiptUrl))
        {
            return BookingResult<BookingDetail>.Fail(BookingFailure.NoReceipt);
        }

        booking.SubmitForVerification(now);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Booking {BookingId} submitted for verification by {CustomerUserId}.",
            bookingId,
            customerUserId);

        // After the save, and deliberately: an email that fails must not undo a
        // submission the customer has already been told went through. A venue
        // that misses the mail still sees the booking in its queue.
        await notifier.PaymentSubmittedAsync(booking, ct);

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
        CancellationToken ct)
    {
        var live = BookingStatuses.Live;

        var now = timeProvider.GetUtcNow();

        var held = await db.BookingSlots
            .AsNoTracking()
            .Where(slot =>
                slot.CourtId == offering.Court.Id &&
                dates.Contains(slot.Date) &&
                live.Contains(slot.Booking.Status) &&
                // The same rule as Booking.HasLapsedAt, asked in SQL: an unpaid
                // hold that has run out with no receipt is not holding anything.
                (slot.Booking.Status != BookingStatus.PendingPayment ||
                    slot.Booking.ReceiptUrl != null ||
                    now < slot.Booking.HoldsUntil))
            .Select(slot => new
            {
                slot.Date,
                slot.StartsAt,
                slot.EndsAt,
                slot.Booking.BookableCourt.CourtSportId,
                slot.Booking.BookableCourt.DivisionNumber
            })
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

    // -------------------------------------------------------------- the rules

    /// <summary>Whether the dates match the kind of booking claimed.</summary>
    private static BookingFailure CheckShape(string kind, IReadOnlyList<DateOnly> dates) => kind switch
    {
        BookingKind.Hourly => dates.Count == 1
            ? BookingFailure.None
            : BookingFailure.KindDoesNotMatchSlots,
        BookingKind.WholeDay => dates.Count == 1
            ? BookingFailure.None
            : BookingFailure.KindDoesNotMatchSlots,
        BookingKind.MultiDay when dates.Count < 2 => BookingFailure.KindDoesNotMatchSlots,
        BookingKind.MultiDay => Consecutive(dates)
            ? BookingFailure.None
            : BookingFailure.DatesNotConsecutive,
        _ => BookingFailure.KindDoesNotMatchSlots
    };

    private static bool Consecutive(IReadOnlyList<DateOnly> dates)
    {
        for (var i = 1; i < dates.Count; i++)
        {
            if (dates[i] != dates[i - 1].AddDays(1))
            {
                return false;
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

        // A whole day is every hour the court is open that day. One hour gone
        // and there is no whole day left to sell -- and "book by the hour
        // instead" is something the customer can act on.
        if (BookingKind.TakesWholeDays(request.Kind))
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
        string? gcashQrCodeUrl) => new(
        booking.Id,
        booking.BookableCourtId,
        booking.Status.ToString(),
        booking.Kind,
        booking.CourtName,
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
        booking.CreatedAt);

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
        /// the offset, and the zone is validated when the facility is saved.
        /// </summary>
        public DateTimeOffset LocalNow(DateTimeOffset utcNow) =>
            TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out var zone)
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
