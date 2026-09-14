using FluentValidation;

namespace IcyPlay.Application.Bookings;

/// <summary>
/// What a customer can book on one court on one date, hour by hour.
///
/// Answered for a whole day at once rather than per hour, because the grid is
/// drawn all at once and sixteen round trips to draw it would be sixteen
/// different moments.
/// </summary>
public sealed record AvailabilityDay(
    Guid BookableCourtId,
    /// <summary>The name a customer sees, derived the same way the listing derives it.</summary>
    string CourtName,
    string FacilityName,
    string SportName,
    DateOnly Date,
    /// <summary>True when the venue is shut that day. No slots follow.</summary>
    bool IsClosed,
    /// <summary>True when the date falls on a configured holiday, which can change every rate.</summary>
    bool IsHoliday,
    /// <summary>True while the court, or its whole venue, is under maintenance.</summary>
    bool IsUnderMaintenance,
    int SlotLengthMinutes,
    int MinimumDurationMinutes,
    /// <summary>What the platform adds per hour. Shown to the customer as the platform fee.</summary>
    decimal PlatformHourlyRate,
    /// <summary>
    /// The court's whole rate card, not just the rates that happen to apply on
    /// this date. A customer looking at a Monday still wants to know the weekend
    /// costs more before they pick a day — reading the rates off the day's slots
    /// would show them only what they already chose.
    ///
    /// Null on any of the three special rates means "same as standard".
    /// </summary>
    decimal? StandardHourlyRate,
    decimal? PeakHourlyRate,
    decimal? WeekendRate,
    decimal? HolidayRate,
    /// <summary>When peak runs. Null until a peak rate is set.</summary>
    TimeOnly? PeakStartsAt,
    TimeOnly? PeakEndsAt,
    bool PeakOnWeekdays,
    bool PeakOnWeekends,
    IReadOnlyCollection<AvailabilitySlot> Slots);

/// <summary>
/// One bookable hour. It carries its own price because the hours of a day do
/// not cost the same: peak, weekend and holiday rates all land here.
/// </summary>
public sealed record AvailabilitySlot(
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    /// <summary>False when somebody already holds this hour, or the court is shut.</summary>
    bool IsOpen,
    /// <summary>
    /// True when the hour has already begun, on the venue's clock.
    ///
    /// Separate from <see cref="IsOpen"/> because the two want different words:
    /// nobody booked eight this morning, it simply went, and a grid that calls
    /// it "booked" tells a customer the court is busier than it is.
    /// </summary>
    bool HasPassed,
    /// <summary>"Standard", "Peak", "Weekend" or "Holiday" — why it costs what it costs.</summary>
    string RateKind,
    /// <summary>Null when the venue has not priced this sport, which makes the hour unsellable.</summary>
    decimal? Rate,
    decimal PlatformFee);

public sealed record CreateBookingRequest(
    Guid BookableCourtId,
    /// <summary>"Hourly", "WholeDay" or "MultiDay". Checked against the slots sent.</summary>
    string Kind,
    IReadOnlyCollection<BookingSlotInput> Slots);

/// <summary>
/// One hour, named by where it starts. The end comes from the court's slot
/// length rather than the client, so a client cannot ask for a longer hour than
/// the venue sells.
/// </summary>
public sealed record BookingSlotInput(DateOnly Date, TimeOnly StartsAt);

public sealed record BookingDetail(
    Guid Id,
    Guid BookableCourtId,
    string Status,
    string Kind,
    string CourtName,
    string FacilityName,
    string SportName,
    /// <summary>
    /// The sport's stable key, for artwork. Read live rather than snapshotted
    /// like the name beside it: the name records what the sport was CALLED when
    /// the booking was made, while the key only ever picks an icon, and the
    /// current icon is the right one to show.
    /// </summary>
    string SportKey,
    /// <summary>First and last date booked. Same value when it is one day.</summary>
    DateOnly StartDate,
    DateOnly EndDate,
    int BookedHours,
    decimal RentalTotal,
    decimal PlatformFeeTotal,
    decimal Total,
    /// <summary>When an unpaid hold lets go of the court.</summary>
    DateTimeOffset HoldsUntil,
    /// <summary>True once the hold has run out without payment being sent.</summary>
    bool HasLapsed,
    /// <summary>The receipt the customer sent, if they have sent one.</summary>
    string? ReceiptUrl,
    /// <summary>
    /// How to pay the venue. Carried on the booking so the checkout is one call
    /// -- and null on both when the venue has not set either up, which is worth
    /// saying out loud rather than showing an empty panel.
    /// </summary>
    string? GcashNumber,
    string? GcashAccountName,
    string? GcashQrCodeUrl,
    IReadOnlyCollection<BookedSlot> Slots,
    DateTimeOffset CreatedAt);

public sealed record BookedSlot(
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    string RateKind,
    decimal Amount,
    decimal PlatformFee);

public enum BookingFailure
{
    None,
    CourtNotFound,
    /// <summary>The court, or its venue, is closed for maintenance.</summary>
    UnderMaintenance,
    /// <summary>The venue has not priced this sport, so there is nothing to charge.</summary>
    NotPriced,
    /// <summary>An hour outside the court's opening hours, or on a day it is shut.</summary>
    OutsideOpeningHours,
    /// <summary>Somebody already holds an hour that clashes with this one.</summary>
    SlotTaken,
    /// <summary>Fewer minutes than the venue's minimum booking.</summary>
    BelowMinimumDuration,
    /// <summary>A whole day was asked for, but not every open hour of it is free.</summary>
    DayNotWhollyAvailable,
    /// <summary>The dates in a multi-day booking have to run one after another.</summary>
    DatesNotConsecutive,
    /// <summary>The hours asked for do not match the kind of booking claimed.</summary>
    KindDoesNotMatchSlots,
    /// <summary>An hour that has already gone.</summary>
    DateInThePast,
    /// <summary>Further ahead than the platform takes bookings.</summary>
    TooFarAhead,
    /// <summary>More hours than one booking is allowed to hold.</summary>
    TooManySlots,
    /// <summary>The hold ran out before payment was sent, and the hours went back on sale.</summary>
    HoldExpired,
    /// <summary>The receipt is not a secure link on the configured Cloudinary account.</summary>
    UntrustedReceiptUrl,
    /// <summary>The receipt has to be a picture. A PDF or a video is not one.</summary>
    ReceiptNotAnImage,
    /// <summary>Nothing to submit: no receipt has been uploaded.</summary>
    NoReceipt,
    /// <summary>The booking is not waiting to be paid for, so this step does not apply.</summary>
    NotAwaitingPayment
}

/// <summary>
/// How far ahead a court can be held.
///
/// A ceiling exists because a hold costs nothing to make and, today, never
/// expires: without one, a single account could sit on a court for a year. The
/// booking page says so on the day strip, and this is what makes that true
/// rather than a sentence the browser tells itself.
/// </summary>
public static class BookingWindow
{
    public const int DaysAhead = 30;
}

public sealed record BookingResult<T>(T? Value, BookingFailure Failure = BookingFailure.None)
{
    public bool Succeeded => Failure == BookingFailure.None;
    public static BookingResult<T> Success(T value) => new(value);
    public static BookingResult<T> Fail(BookingFailure failure) => new(default, failure);
}

/// <summary>The receipt, as the browser reports it after uploading to Cloudinary.</summary>
public sealed record AttachReceiptRequest(string ReceiptUrl);

public sealed class AttachReceiptRequestValidator : AbstractValidator<AttachReceiptRequest>
{
    public AttachReceiptRequestValidator()
    {
        RuleFor(x => x.ReceiptUrl).NotEmpty().MaximumLength(1000);
    }
}

public sealed class BookingSlotInputValidator : AbstractValidator<BookingSlotInput>
{
    public BookingSlotInputValidator()
    {
        RuleFor(x => x.Date).NotEmpty();
    }
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    /// <summary>
    /// A month of a busy court, which is past any real booking and short of
    /// anything that would make the conflict check expensive.
    /// </summary>
    public const int MaximumSlots = 500;

    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.BookableCourtId).NotEmpty();
        RuleFor(x => x.Kind)
            .NotEmpty()
            .Must(IcyPlay.Domain.Bookings.BookingKind.IsSupported)
            .WithMessage("Choose hourly, a whole day, or several days.");
        RuleFor(x => x.Slots).NotEmpty();
        RuleForEach(x => x.Slots).SetValidator(new BookingSlotInputValidator());
        RuleFor(x => x.Slots)
            .Must(slots => slots.Count <= MaximumSlots)
            .WithMessage($"A booking can hold at most {MaximumSlots} hours.");
        RuleFor(x => x.Slots)
            .Must(slots => slots
                .Select(slot => (slot.Date, slot.StartsAt))
                .Distinct()
                .Count() == slots.Count)
            .WithMessage("The same hour was sent twice.");
    }
}
