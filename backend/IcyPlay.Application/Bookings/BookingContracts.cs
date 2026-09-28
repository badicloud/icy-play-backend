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
    IReadOnlyCollection<AvailabilitySlot> Slots)
{
    /// <summary>
    /// Whether this day can be sold open to close.
    ///
    /// The same rule <see cref="DayOutlook.CanBeHiredWhole"/> states from a
    /// count of hours, said here from the hours themselves. Both exist because
    /// the strip and the grid ask at different depths; they must never
    /// disagree, because a day picker that offers a day the booking refuses is
    /// a customer told no after choosing.
    /// </summary>
    public bool CanBeHiredWhole =>
        !IsClosed && !IsUnderMaintenance && Slots.Count > 0 && Slots.All(slot => slot.IsOpen);
}

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
    /// <summary>
    /// The upgrade still open on this booking, if there is one, and which
    /// court it is asking for.
    ///
    /// A booking with one is settled and about to change at the same time, and
    /// the status on its own cannot say both: "Confirmed" is true and hides
    /// the fact that a move is waiting. Null when nothing is open.
    /// </summary>
    string? UpgradeStatus,
    string? UpgradeToCourtName,
    /// <summary>
    /// What the open move asks the customer to pay: nought on a free move,
    /// which goes straight to the venue, and the difference on an upgrade.
    /// Null when nothing is open.
    /// </summary>
    decimal? UpgradeBalanceDue,
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
    /// How many days before it starts this venue stops taking moves — the
    /// venue's own dial, so the page can say why the button has gone.
    /// </summary>
    int MoveNoticeDays,
    /// <summary>
    /// Whether it can be moved right now — which is more than having moves
    /// left: the venue's notice before it starts closes the door, and a move
    /// already waiting on the venue has to be answered first. Answered here
    /// rather than worked out in the browser, so the rule lives in one place
    /// and the reader's clock cannot disagree with the venue's.
    /// </summary>
    bool CanBeMoved,
    /// <summary>
    /// Whether it has not started and is already inside the venue's notice, so
    /// moves are closed until — and unless — it is under way.
    /// </summary>
    bool IsInsideMoveNotice,
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
    /// <summary>
    /// Whether one of the booked hours is running right now, on the venue's
    /// clock.
    ///
    /// Not the same question as <see cref="IsInPlay"/>, which asks only whether
    /// the first hour has begun and stays true for ever afterwards — a booking
    /// played last March is still "in play" by that reading. This one ends when
    /// the hours do, which is what a customer's list means by a booking being
    /// on: the court is theirs at this moment and they should be at it.
    ///
    /// Inside an hour rather than merely between the first and the last, so a
    /// booking of one o'clock and four o'clock does not claim the customer is
    /// on court at three, when they were sold nothing.
    ///
    /// Confirmed only. Hours passing on a booking the venue has not accepted is
    /// not a session in progress, it is a booking about to be refused.
    ///
    /// Answered here for the same reason as the two above it: the reader's
    /// clock is not the venue's, and a browser eight hours out would light this
    /// up on the wrong third of the day.
    /// </summary>
    bool IsInProgress,
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
    /// <summary>
    /// A multi-day run reached over a day it could have had.
    ///
    /// A run passes over days it cannot be sold — a day somebody else has part
    /// of, a day the venue is shut — and those are neither booked nor charged.
    /// Reaching over a day that was free is a different thing: that is a set of
    /// days rather than a run, and it is not what this sells.
    /// </summary>
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
    /// <summary>No reason given, or not one from the list.</summary>
    MoveReasonRequired,
    /// <summary>"Other", with nothing said about what the other was.</summary>
    MoveReasonNoteRequired,
    /// <summary>A note longer than a reason needs to be.</summary>
    MoveReasonNoteTooLong,
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
    /// A free move collects no money on the way, so a dearer court cannot be
    /// asked for through it without somebody quietly paying the difference —
    /// and that somebody would be the venue. Dearer hours are an upgrade.
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
    NothingWouldChange,
    /// <summary>
    /// A booking sold by the day, once that day has begun.
    ///
    /// An hourly booking under way still has whole hours ahead of it, and
    /// moving those to another court is the most useful thing a move does: a
    /// floodlight fails at two and the afternoon is saved. A day taken open to
    /// close has no such remainder to speak of. What is left of it is the part
    /// of a day nobody chose — a customer who booked Saturday and is moved at
    /// noon has bought a morning on one court and an afternoon on another,
    /// which is not the thing they bought.
    /// </summary>
    DayBookingInPlay,
    /// <summary>
    /// The booking has not started and is inside the venue's notice. The hours
    /// it would give back are too close to be sold again.
    /// </summary>
    TooLateToMove,
    /// <summary>
    /// The booking is under way, so its hours are fixed. What is left of it can
    /// change court, and only court.
    /// </summary>
    CourtOnlyOnceStarted
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
    IReadOnlyCollection<BookingSlotInput>? Slots = null,
    /// <summary>
    /// Why, from <c>MoveReason</c>. Required: asked so the venue can count the
    /// answers, and a question that may be skipped is one most people skip.
    /// </summary>
    string? Reason = null,
    /// <summary>A few words. Required when the reason is Other, optional otherwise.</summary>
    string? ReasonNote = null);

/// <summary>
/// The hours a move may be placed on, on one date, before any court is chosen.
///
/// The move screen used to ask for a court first and draw that court's grid.
/// It asks the other way round now — a date, then hours, then the courts that
/// can actually take them — and that leaves a step with no court to ask about.
/// So the hours come from the building rather than from any floor in it: the
/// facility's own opening hours, cut into slots the length this booking's
/// hours already are.
///
/// It prices nothing. What an hour costs depends on the court, and no court
/// has been picked yet; the prices arrive with <see cref="MoveOption"/>, one
/// per court, once the hours are known.
/// </summary>
public sealed record MoveWindow(
    DateOnly Date,
    /// <summary>The venue is shut that day. No slots follow.</summary>
    bool IsClosed,
    bool IsHoliday,
    /// <summary>The length of this booking's hours, which the new ones must match.</summary>
    int SlotLengthMinutes,
    /// <summary>
    /// How many slots have to be picked.
    ///
    /// The hours still to be played, not the hours the booking has: a move
    /// changes when and where a booking is, never how much of it there is, and
    /// on a booking under way the hours already played are not moving.
    /// </summary>
    int SlotsNeeded,
    IReadOnlyCollection<MoveWindowSlot> Slots);

/// <summary>
/// One hour the building is open for, said without reference to any court.
/// </summary>
public sealed record MoveWindowSlot(
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    /// <summary>
    /// The hour has begun on the venue's clock, so it cannot be moved onto.
    ///
    /// Said rather than left out, because a grid that silently starts at two
    /// o'clock reads as a venue that opens at two.
    /// </summary>
    bool HasPassed);

/// <summary>
/// Which courts could take this booking, and what each would cost.
///
/// The answer to the question the move screen now asks third: the date and the
/// hours are settled, and this says where they can go. Every court in the list
/// is one the move would actually be allowed onto — shut courts, courts under
/// maintenance and courts whose hours are already spoken for do not appear at
/// all, because a card that cannot be clicked is a question the customer has
/// to answer twice.
/// </summary>
public sealed record MoveOptions(
    /// <summary>
    /// Whether the booking has begun, on the venue's clock. A booking under
    /// way keeps its hours — only the court can change.
    /// </summary>
    bool IsInPlay,
    /// <summary>Hours already played. They stay where they were, at what they cost.</summary>
    int HoursStaying,
    int HoursMoving,
    /// <summary>
    /// The hours being placed, as the server counted them.
    ///
    /// Sent because on a booking under way the browser cannot work them out:
    /// which hours are still to play is a question about the venue's clock,
    /// and the browser's would name an hour the customer is standing through.
    /// </summary>
    IReadOnlyCollection<BookedSlot> MovingSlots,
    IReadOnlyCollection<MoveOption> Courts);

/// <summary>
/// One court the booking could go to, priced for the hours asked about.
///
/// The price is on the card rather than fetched when a card is tapped. A
/// customer choosing between four courts is choosing on price as much as on
/// name, and four round trips to find that out is four chances to be shown a
/// figure that has since moved.
/// </summary>
public sealed record MoveOption(
    Guid BookableCourtId,
    string CourtName,
    string SportName,
    /// <summary>What this court charges an hour, for the card's small print.</summary>
    decimal? StandardHourlyRate,
    /// <summary>
    /// The court the booking is on now.
    ///
    /// It appears in the list when the hours would change — keeping the court
    /// and changing the time is a real move — and is left out when they would
    /// not, because that is not a move at all.
    /// </summary>
    bool IsCurrentCourt,
    /// <summary>What the hours being moved come to now, in court rental alone.</summary>
    decimal MovingRentalNow,
    /// <summary>What they would come to here.</summary>
    decimal MovingRentalNew,
    /// <summary>The shortfall, and never less than nothing: cheaper is not a refund.</summary>
    decimal BalanceDue,
    /// <summary>
    /// The hours this booking would land on here, priced on this court.
    ///
    /// Carried per court because on a booking sold by the day the browser
    /// cannot work them out: a day is whatever THIS court is open for, and
    /// two courts in one building need not keep the same hours. They are
    /// what the move or the upgrade is then asked for, so what was priced
    /// on the card is what gets sent.
    /// </summary>
    IReadOnlyCollection<BookedSlot> Slots,
    /// <summary>Minutes this court is held for while a difference is paid.</summary>
    int HoldMinutes)
{
    /// <summary>
    /// Dearer than what has been paid, so it cannot simply be moved onto.
    ///
    /// The move itself refuses these — nothing collects money on the way — and
    /// they go through the upgrade instead: the venue is asked, the difference
    /// is paid, and the booking moves when that clears. They stay in the list
    /// because a customer who would happily pay for a better court should be
    /// able to see it and choose it.
    /// </summary>
    public bool IsUpgrade => BalanceDue > 0m;
}

/// <summary>
/// Where to look for courts.
/// </summary>
/// <param name="Dates">
/// The dates a booking sold by the day would move onto: one for a whole day,
/// as many as it has now for a run of them.
///
/// Only day bookings send them. Their hours are not a choice but whatever
/// each court is open for, so the dates are all there is to ask with and the
/// hours are worked out per court.
///
/// Named one by one rather than as a start and a length, because the picker
/// lets each be chosen and unchosen on its own. A run may therefore arrive
/// with a gap in it. That is refused when a booking is first made -- a run
/// with a hole is two bookings wearing one name -- but a move is not a sale,
/// the number of days cannot change, and nothing is being bought that was
/// not already paid for.
/// </param>
/// <param name="Slots">
/// The hours wanted, which carry their own date. Null keeps the ones the
/// booking already has — what a booking under way needs, since its hours are
/// not moving in time, only in place.
/// </param>
public sealed record MoveOptionsRequest(
    IReadOnlyCollection<DateOnly>? Dates = null,
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
    /// <summary>
    /// The same comparison, narrowed to the hours that are actually going
    /// somewhere — <see cref="HoursMoving"/> of them.
    ///
    /// The two totals above cover the whole booking. Hours already played sit
    /// on both sides of them and cancel, so the balance due is the same figure
    /// either way; what those totals cannot do is be shown to anybody. A
    /// customer moving the last hour of a booking that ran from one o'clock
    /// does not recognise "you are paying 1,000 now" — most of that thousand
    /// is behind them and not in question. This pair names only the part that
    /// is, which is what the move screen puts on the page.
    /// </summary>
    decimal MovingRentalNow,
    decimal MovingRentalNew,
    /// <summary>
    /// Those same hours, named, on the court they would move to and at the
    /// price quoted for them.
    ///
    /// Sent because the checkout has to show what is being paid for, and on a
    /// booking under way it cannot work that out for itself: which hours are
    /// still to play is a question about the venue's clock, and the browser
    /// asking its own would list an hour the customer is standing through. The
    /// server has already decided; this is the decision.
    /// </summary>
    IReadOnlyCollection<BookedSlot> MovingSlots,
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
