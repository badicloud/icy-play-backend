using FluentValidation;

namespace IcyPlay.Application.Bookings;

/// <summary>
/// One date in the booking window, in the least detail a day picker needs.
///
/// The hour grid answers one date at a time, which is right for the grid and
/// useless for the strip of days above it: a customer choosing a whole day
/// should not be able to pick a day that cannot be sold whole and be told so
/// afterwards. Pricing thirty grids to find that out would be thirty times the
/// work to show a number nobody reads, so this counts hours instead.
/// </summary>
public sealed record DayOutlook(
    DateOnly Date,
    /// <summary>The venue is shut that day. No hours at all.</summary>
    bool IsClosed,
    bool IsHoliday,
    /// <summary>The court, or its whole venue, is closed for work that day.</summary>
    bool IsUnderMaintenance,
    /// <summary>Hours still free.</summary>
    int OpenHours,
    /// <summary>Hours the court is open, taken or not.</summary>
    int TotalHours)
{
    /// <summary>
    /// A day sold open to close needs every hour of it free: one hour gone and
    /// the day is no longer whole. The same rule the booking itself applies, so
    /// the picker and the server cannot disagree about which days are on offer.
    /// </summary>
    public bool CanBeHiredWhole =>
        !IsClosed && !IsUnderMaintenance && TotalHours > 0 && OpenHours == TotalHours;
}

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
    /// <summary>
    /// Which venue, so a move can offer the other courts in the same building.
    /// The name beside it is a snapshot of what the venue was called; this is
    /// the venue itself.
    /// </summary>
    Guid FacilityId,
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
    /// <summary>
    /// How to reach the venue, when reaching them is the only way forward.
    ///
    /// A venue that has set up no way of being paid cannot take a receipt, and
    /// telling somebody to get in touch without saying how leaves them to go
    /// and find the venue themselves. Null on both when the venue has given
    /// neither.
    /// </summary>
    string? ContactPhone,
    string? ContactEmail,
    /// <summary>
    /// Why it ended, when it did: the venue's words on a refusal, or the
    /// customer's own on a cancellation.
    ///
    /// The desk is made to write one before it can refuse anything, and this is
    /// what that was for. "Not accepted" on its own tells somebody their money
    /// is coming back and not why, which is the one question they will ring up
    /// to ask.
    /// </summary>
    string? CancellationReason,
    IReadOnlyCollection<BookedSlot> Slots,
    /// <summary>
    /// How many moves this booking has left. Zero once they are used up, and
    /// the booking then stays where it is.
    /// </summary>
    int MovesLeft,
    /// <summary>
    /// How many it was allowed in all, which is the venue's own dial.
    ///
    /// Sent alongside the remainder so the customer can be told what the
    /// number means. "3 more times" on its own invites the question "three of
    /// how many, and what happens at the end of them" — and the answer to the
    /// second is that the booking can no longer be moved, which somebody ought
    /// to hear before they spend their last one.
    /// </summary>
    int MoveLimit,
    /// <summary>
    /// Whether it can be moved right now — which is more than having moves
    /// left, because the last day before it starts closes the door. Answered
    /// here rather than worked out in the browser, so the rule lives in one
    /// place and the reader's clock cannot disagree with the venue's.
    /// </summary>
    bool CanBeMoved,
    /// <summary>
    /// Whether the booking has started, on the venue's clock.
    ///
    /// A booking in play can still change court — a floodlight fails and the
    /// game carries on next door — but it cannot change when it is. The hours
    /// are being played as they are read. Answered here rather than worked out
    /// in the browser, because the reader's clock is not the venue's and this
    /// decides what the move screen is allowed to offer.
    /// </summary>
    bool IsInPlay,
    DateTimeOffset CreatedAt);

/// <summary>
/// One thing that happened to a booking.
///
/// Read from the platform's audit trail rather than from a record of its own:
/// the venue's desk already writes what it does there, and two accounts of one
/// booking are two accounts that will one day disagree.
///
/// What the trail keeps and this does not: who did it, from which address, on
/// which browser. That is for the platform to answer questions with, not for
/// the person whose booking it is.
/// </summary>
public sealed record BookingHistoryEntry(
    /// <summary>The stable name of what happened, for picking wording and artwork.</summary>
    string Action,
    /// <summary>Said plainly, because this is read by the person it happened to.</summary>
    string Description,
    /// <summary>Why, when whoever did it gave a reason. Null when they did not.</summary>
    string? Reason,
    DateTimeOffset At);

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
    /// <summary>Only a booking that still stands can be moved.</summary>
    NotMovable,
    /// <summary>The venue's limit on moves for one booking has been reached.</summary>
    MoveLimitReached,
    /// <summary>
    /// Every hour of it has been played. There is nothing left to move, and a
    /// booking that is over is a refund rather than a move.
    /// </summary>
    BookingFinished,
    /// <summary>
    /// This booking is already waiting on a move. Two at once and the customer
    /// and the venue can be sending it to different courts.
    /// </summary>
    MoveAlreadyRequested,
    /// <summary>No move is waiting on this booking, or it has already been settled.</summary>
    MoveRequestNotFound,
    /// <summary>The difference has not been paid, so there is nothing to confirm yet.</summary>
    MoveNotPaid,
    /// <summary>The receipt has to be a picture. A PDF or a video is not one.</summary>
    ReceiptNotAnImage,
    /// <summary>
    /// The venue has set up no way to be paid, so there is no payment a receipt
    /// could be of.
    ///
    /// Taking one anyway would put a picture in front of a desk that has no
    /// account to check it against, and tell the customer their money is on its
    /// way to somebody who never asked for it.
    /// </summary>
    VenueCannotBePaid,
    /// <summary>Nothing to submit: no receipt has been uploaded.</summary>
    NoReceipt,
    /// <summary>The booking is not waiting to be paid for, so this step does not apply.</summary>
    NotAwaitingPayment,
    /// <summary>
    /// The court asked for costs more than the booking has been settled for.
    ///
    /// A move happens at once now, and nothing collects money on the way: the
    /// venue is not asked to approve it and the customer is not sent to a
    /// checkout. So a dearer court cannot be moved onto without somebody
    /// quietly paying the difference, and that somebody would be the venue.
    /// </summary>
    MoveCostsMore,
    /// <summary>
    /// The court offered is not the same sport, or is not at the same venue.
    ///
    /// A move changes which floor the hours are on. It does not change what was
    /// bought: a booking carries the sport and the venue as they were named and
    /// priced when the agreement was made, and moving pickleball onto a
    /// badminton court would leave a record of a thing that never happened.
    /// </summary>
    NotTheSameOffering,
    /// <summary>
    /// The hours asked for cost the same or less, so there is nothing to
    /// upgrade. That move happens at once and free, through the move screen.
    /// </summary>
    NothingToUpgrade,
    /// <summary>
    /// The same court, at the same hours. Nothing would change.
    ///
    /// It used to go through: the booking was rewritten with what it already
    /// had, and the venue's move limit was charged for it. Three of those and
    /// a customer had spent every move they were allowed without their booking
    /// ever having moved.
    /// </summary>
    NothingWouldChange
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

/// <summary>
/// Where a booking is going.
///
/// One date, because nothing else may change: the hours of the day, the number
/// of days and the court all stay as they were. That is what keeps the price
/// identical, which is what lets a move happen without a second payment.
/// </summary>
/// <summary>
/// Where a booking would rather be played.
///
/// The court changes; the hours do not. A customer moving to another court
/// wants the same slot on another floor, and a venue moving somebody off a
/// failed court has no interest in rescheduling them as well.
/// </summary>
/// <summary>
/// Where a booking is going, and optionally when.
/// </summary>
/// <param name="Slots">
/// The hours to move onto. Null keeps the ones the booking already has, which
/// is what a game under way needs: the court changes and the clock does not.
/// Given, they replace the hours still to be played — the same number of them,
/// because a move changes when and where a booking is, never how much of it
/// there is.
/// </param>
public sealed record MoveBookingRequest(
    Guid ToBookableCourtId,
    IReadOnlyCollection<BookingSlotInput>? Slots = null);

/// <summary>
/// What a move would cost before anybody commits to it.
///
/// Asked and answered before the customer says yes, because "move this
/// booking" and "move this booking and pay another twelve hundred pesos" are
/// different questions and only one of them can be answered with a tap.
/// </summary>
public sealed record MoveQuoteResponse(
    Guid ToBookableCourtId,
    string ToCourtName,
    /// <summary>Hours already played. They stay where they were, at what they cost.</summary>
    int HoursStaying,
    int HoursMoving,
    /// <summary>
    /// What this booking's hours come to now, in court rental alone.
    ///
    /// The platform fee is left out of both sides of this comparison. It is
    /// charged per hour booked, and a move buys no hours — the same number of
    /// them end up somewhere else. Counting it would make an identical move
    /// look like it cost something.
    ///
    /// Taken from the booking's own hours rather than from what was settled:
    /// what was settled can be nothing at all on a booking still being paid
    /// for, and comparing against nothing makes every move look like a bill.
    /// </summary>
    decimal RentalNow,
    /// <summary>What they would come to on the new court, at the new hours.</summary>
    decimal RentalNew,
    /// <summary>The shortfall, and never less than nothing: cheaper is not a refund.</summary>
    decimal BalanceDue,
    /// <summary>Minutes the new court is held for while the difference is paid.</summary>
    int HoldMinutes,
    /// <summary>
    /// Whether the booking has begun, on the venue's clock.
    ///
    /// What the move screen reads to decide whether to offer dates at all: a
    /// booking under way can change court but not when it is, and one that has
    /// not started can change both.
    ///
    /// This used to ask whether the hours fell on the venue's today, which is
    /// a different and stricter question. A booking at eight tonight is today
    /// and has not begun, and there was never a reason it could not be carried
    /// to tomorrow — the server would have taken it. Only this screen said no.
    ///
    /// Answered per request rather than once with the booking, because it
    /// turns over while the screen is open.
    /// </summary>
    bool IsInPlay)
{
    public bool IsUpgrade => BalanceDue > 0m;
}

/// <summary>
/// An upgrade a customer has asked for, and how far it has got.
///
/// Read back on every visit rather than held on the screen that created it, so
/// a refresh, a second tab, or somebody coming back after paying all land on
/// the step they are actually at.
/// </summary>
public sealed record UpgradeRequestResponse(
    Guid Id,
    Guid BookingId,
    Guid ToBookableCourtId,
    string ToCourtName,
    /// <summary>What the booking's hours come to now, in court rental alone.</summary>
    decimal RentalNow,
    /// <summary>What the asked-for hours come to, at the price quoted.</summary>
    decimal RentalNew,
    /// <summary>
    /// The difference, fixed when the upgrade was asked for.
    ///
    /// Not worked out again when it is paid: a rate the venue changes in
    /// between must not change what somebody has already been asked for.
    /// </summary>
    decimal BalanceDue,
    string Status,
    /// <summary>When the hours being asked for go back on sale.</summary>
    DateTimeOffset HoldsUntil,
    /// <summary>
    /// Whether that clock has run out with nothing paid.
    ///
    /// Answered here rather than left to the browser, because the browser's
    /// clock is not the one this was timed against.
    /// </summary>
    bool HasLapsed,
    string? ReceiptUrl,
    /// <summary>Why the venue said no, when it did.</summary>
    string? DeclineReason,
    IReadOnlyCollection<UpgradeSlotResponse> Slots);

/// <summary>One hour an upgrade is asking for, at the price it was quoted.</summary>
public sealed record UpgradeSlotResponse(
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    /// <summary>The court rental for this hour. The platform fee is not charged again.</summary>
    decimal Amount);

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
