# Booking

## Purpose

How an hour of a court is sold: what is free, what it costs, what taking it
does, and what may still be done to it afterwards. Related:
[courts-and-pricing.md](courts-and-pricing.md),
[platform-fee-strategy.md](platform-fee-strategy.md).

**This is where the booking rules live.** [api-design.md](api-design.md) says
what the endpoints take and answer; it does not restate the rules, because a
rule written twice is a rule that will one day only be half true. The move
rules used to live there, and had drifted from the code within a week.

---

## What a booking points at

A **bookable court** — a court set up for a sport, or one marked-out part of it.
Not a court: a floor taking basketball, volleyball and pickleball three across
is five bookable courts, and each is sold on its own. See
[courts-and-pricing.md](courts-and-pricing.md#bookable-courts).

A booking is a row plus one row per hour. Almost everything on it is a
**snapshot** — the court's name, the rate charged, the platform's per-hour fee —
because a booking records an agreement made at a moment. Renaming the court or
re-pricing the sport next week must not reach backwards and rewrite what
somebody already agreed to pay. This is the opposite of how the catalogue works,
where a rename is meant to flow through everywhere.

Hours are priced **one at a time**, not as a block. Six in the morning and six
in the evening are the standard and the peak rate, and a customer who books both
is owed a bill that says so rather than an average.

---

## The three kinds

`Booking.Kind`, one of three. It is **stored rather than worked out from the
hours**, and that is the whole reason it exists: a whole day means "every hour
the court was open *that day*", and the day the venue shortens its hours, a
booking that was a whole day would start reading as a handful of hours.

| Kind | What it is | How many dates |
| --- | --- | --- |
| `Hourly` | Hours picked one at a time. They need not run back to back. | Exactly one |
| `WholeDay` | Every hour the court is open on one date. | Exactly one |
| `MultiDay` | A run of whole days. | Two or more |

The client sends the kind it is claiming and the server checks the claim against
the hours — `CheckShape` for the dates, `CheckSlots` for the hours themselves. A
claim that does not match is `KindDoesNotMatchSlots`, not a silent correction: a
customer who thinks they bought a whole day and holds six hours has been sold
something else.

### Whole days are whole

For `WholeDay` and `MultiDay`, **every hour the court is open that day must be
free**, and all of them are taken. One hour gone and there is no whole day left
to sell — the refusal is `DayNotWhollyAvailable`, and "book by the hour instead"
is something the customer can act on.

A run used to take each day for whatever was still free on it. What that sold
was the problem: a "week" that quietly missed an afternoon, priced as though it
had not. A run is now whole days or it is not a run, and a week with a hole in
it is two bookings that say what they are.

### What a run may pass over

A `MultiDay` run is a stretch of the calendar, and a day inside it may be
**passed over only when it could not have been had whole** — the venue is shut,
the court is closed for work, or an hour of it is already somebody else's. A
venue closing one day a week could otherwise never sell a run longer than six
days.

A day that **was** free may not be skipped. Reaching over it is a *set of days*
rather than a run, which is a different thing to sell and a different thing to
price, and nothing here offers it. The test is the day's own
`AvailabilityDay.CanBeHiredWhole`, which is the same question the picker asks
before it greys a day out — so the screen and the server agree about which days
are crossable.

The days passed over are not booked, not charged, and not quietly swallowed
either: the customer is shown which days they are getting and pays for those.
Somebody wanting the Thursday and the Saturday with the Friday already gone gets
one booking for two days, rather than two bookings with two holds, either of
which they can lose.

---

## What one hour costs

The day sets the base, and the peak window lifts it:

1. A **holiday** → the holiday rate
2. Otherwise a **weekend** → the weekend rate
3. Otherwise → the standard rate
4. Then, if the hour is inside the **peak window** and peak applies to that day
   type, peak replaces it — **but only when peak is the higher number**

A blank special rate means "same as standard", not free.

`CourtSport.PriceAt` is the only place this is decided, and the availability
grid and the bill both read it. A customer shown one number and charged another
has been lied to.

**What this cannot express:** a cheaper weekend evening. Peak is one absolute
number for every day it applies to, so a venue that discounts weekends still
charges peak inside the window. A test pins that behaviour so it reads as
deliberate rather than as an accident. Fixing it would need a peak rate per day
type, which nothing has asked for.

---

## What is free

`GET /api/v1/catalog/bookable-courts/{id}/availability?date=` — anonymous, and
never cached. A visitor should see whether Saturday morning is free before being
asked to make an account; a grid a minute stale is a customer picking an hour
that has just gone.

An hour is on sale when all of these hold:

* the venue is open that day (the court's own hours, or the building's)
* neither the court nor its venue is under maintenance
* the sport has a standard rate
* **nothing clashing holds it**

That last one is the rule the whole model exists for: one floor hosts one sport
at a time, and within that sport its parts run side by side. Basketball being
played blocks all three pickleball courts; pickleball part one leaves parts two
and three on sale. `BookableCourt.ClashesWith` is the one place it is written.

A closed court still shows its hours and prices — a customer deciding where to
play next month still wants to know — but none of them are open.

**An hour that has begun is gone**, because such an hour cannot be sold. That
fact returns under moving, where it is the reason an hour in progress stays
where it is.

---

## Taking it

`POST /api/v1/bookings`, signed in as a customer. The client sends the hours it
wants and which kind it is claiming.

Every hour is **priced again on the server**. What the customer was shown is a
quote; the only number that binds anyone is the one written here.

### Refusals, in the order they are asked

The order is deliberate, because the answer is advice. A customer told "that
hour has gone, pick again" when they asked for a whole day has been pointed at a
door that is not there.

1. the date has gone, or the kind does not match the dates sent
2. maintenance, or a day the venue is shut
3. an hour outside opening hours, or a sport with no price
4. **a whole day with an hour already gone** — "book by the hour instead"
5. an hour somebody else holds
6. shorter than the venue's minimum booking

### Two customers at once

Creation runs in a **serializable** transaction, and the conflict check happens
inside it. Two customers reading "free" a millisecond apart and both writing is
exactly how one court gets sold twice.

---

## Status

`PendingPayment` → `PendingVerification` → `Confirmed`, or `Rejected`,
`Cancelled`, `Expired`.

The first three **hold the court**; the rest have let go of it and the hours are
back on sale. `BookingStatuses.IsLive` is the one place that is decided, so "is
this hour free" is answered the same way everywhere it is asked.

Cancelling does not delete. A cancelled booking is part of the record; the hours
simply go back on sale.

**Rejection is its own state, not a cancellation.** A customer who changed their
mind and a receipt that did not add up are different things, and a venue reading
its own history has to be able to tell them apart. The court goes back on sale
either way, because neither still holds it.

**A rejection takes a reason from a list** (`RejectReason`):
`PaymentNotReceived`, `WrongAmount`, `ReceiptUnclear`, `CourtNotAvailable`,
`Other`. Picking one is required; a note is optional, up to 200 characters,
except on `Other`, which needs one. The refusals are `RejectReasonRequired`,
`RejectNoteRequired` and `RejectNoteTooLong`. A list for the same reason moves
have one: the desk's *Declined Bookings* report counts the answers.

The booking keeps the pick (`RejectionReason`), the note (`RejectionNote`) and
who said no (`RejectedByUserId`). `CancellationReason` holds the one sentence a
person reads — "Wrong amount — paid ₱4,800 only" — which is what the customer
sees on their booking and what the history shows, so every screen that already
read it carries on unchanged. Rejections from before the list have only the
words the desk typed, and the report shows them as **not categorised**.

---

## Paying for it

The customer pays the **venue** by GCash — the number, account name and QR code
held on the facility owner. Nothing in this platform ever holds the customer's
money, which is the fact the whole of moving hangs off.

1. The booking is created `PendingPayment` and **holds the court for
   `PartialBookingExpiryMinutes`** — default **5**, clamped 5–240, set per
   owner.
2. The customer **uploads the receipt** (`Booking.AttachReceipt`).
3. The customer **submits it for verification**. `PendingVerification`.
4. Somebody at the venue **confirms or rejects** it at the desk.

**The default was half an hour and is now five minutes.** The reasoning for
thirty was that somebody should be able to open GCash and pay without hurrying.
Venues came back and said half an hour is a court sitting dark on a Saturday
because somebody wandered off, and that paying actually takes a minute or two.
Their floor, their call — and five is also the floor of the range, because under
it a customer is racing the clock rather than paying and every abandoned payment
is a complaint. The ceiling is four hours: a day is not a hold, it is a free
reservation, and the whole point is that it is neither.

**The receipt stops the clock, not the submit button.** Between uploading and
pressing submit there is nothing left to do, and somebody who uploaded at minute
twenty-nine must not lose a court they have already paid for because they read
the page for two minutes first. `Booking.HoldsTheCourtAt` takes the moment
rather than reading a status somebody has to remember to write.

**Lapsing is derived, not stored.** A hold is gone when the moment passes and no
receipt has arrived; nothing has to run on time for that to be true. No
background job sweeps expired bookings — one would be a second thing to keep
running and a second answer to the same question.

`Booking.Confirm` sets `PaidTotal` to the booking's total. That figure is what
an upgrade later asks its difference against; without it the customer would be
charged the whole of the new court.

---

## Moving a booking

`POST /api/v1/bookings/{id}/move`, the customer's own booking only.

**There is no cancellation and no refund anywhere in this platform**, because
nothing ever holds the customer's money. Moving is what is offered instead — and
that is why it is generous about where a booking may go, and strict about what
it may cost.

A move is **immediate**. The customer picks a court and it happens: the venue is
not asked to approve it, and the customer is not sent to a checkout. Asking a
venue to approve every change put people in a queue nobody was answering.

### What may change

The court, the hours, the dates, or any combination. `Booking.MoveTo` takes a
target court and a new set of slots.

What may **not** change is the sport and the venue. Every route into a move
comes through `QuoteAsync`, so that rule holds in one place: without it a
pickleball booking could be sent to a badminton court in another building, and
the booking would keep saying "Pickleball" and the old venue's name, because
those are recorded on it as they were sold. The refusal is `NotTheSameOffering`.

### The rules, in the order they are asked

| Rule | Refusal |
| --- | --- |
| **Confirmed only.** A booking whose payment the venue has not checked might still be turned down, and moving one shuffles courts around an agreement that may never stand. An unpaid hold needs no moving: let it lapse and book the other date. | `NotMovable` |
| **Within the venue's move limit.** `FacilityOwner.MoveLimit` — default 3, clamped 1–20. | `MoveLimitReached` |
| **Same sport, same venue.** | `NotTheSameOffering` |
| **A day booking whose day has begun does not move.** | `DayBookingInPlay` |
| **Something must still be ahead of it.** Every hour played is a refund, and there are none. | `BookingFinished` |
| **The shape is kept** — see per kind, below. | `KindDoesNotMatchSlots` |
| **The hours are open on the target court.** | `SlotTaken`, `OutsideOpeningHours`, `NotPriced` |
| **It must not cost more.** | `MoveCostsMore` → offer an upgrade |
| **It must actually change something.** | `NothingWouldChange` |
| **The customer says why** — see below. | `MoveReasonRequired`, `MoveReasonNoteRequired`, `MoveReasonNoteTooLong` |

### Why it moved

Every move asks the customer for a reason, picked from a short list:
`ScheduleChanged`, `Weather`, `CourtProblem`, `DifferentCourt`, `Other`
(`MoveReason` in the domain). **Picking one is required.** A note is optional,
up to 200 characters — **except on `Other`, which needs a few words**, so it is
not a way of saying nothing.

A list rather than a free box because the point of asking is to count the
answers: "rain", "raining" and "ulan" typed into a box are three answers to a
report and one answer to a person.

The reason goes to three places:

* **The booking's history**, at the end of the move's line ("Reason: Weather."),
  which the customer and the desk both read.
* **`BookingMoves`**, one row per move that went through, which the desk's
  *Moved Bookings* report counts.
* On an upgrade, **the request itself**, because it is asked when the upgrade is
  and only counted when the desk approves it — and the desk approving it is not
  who knows why.

Moves made before customers were asked have no reason, and the report says
**Not asked** rather than guessing.

**Why a limit at all.** A booking that can be carried forward for ever is an
option on a venue's calendar rather than a booking, and the venue is the one
turning other people away to keep holding it. It is set per venue because it is
their court being held while somebody makes up their mind.

A move the **venue itself** asked for would not be counted against it: it is not
the customer's doing, and spending their allowance on the venue's flooded court
would be charging them for it twice. That is the `countsAgainstTheLimit`
argument on `MoveTo`.

**Nothing passes it `false` today.** The attendant's side of a move was taken
out, and both remaining callers — the customer's move and an approved upgrade —
count. The argument is kept because the rule it carries is the right one and the
capability is expected back; until then a venue that has to move somebody does
it by asking them to move, and the customer spends their own allowance on it.
The booking policy shown to customers still promises the venue can do this, and
that promise is currently false — see [web-app.md](web-app.md).

### A booking that is being played

**Confirmed covers both before it starts and while it is under way.** A court
that floods at two o'clock is exactly when a move is worth most.

An hour counts as played once it has **begun**, not once it has finished, and a
played hour **stays where it was played, at what it cost**. The hour running as
somebody presses Move is half spent on a court they are standing on: carrying it
to another floor would sell them the whole of it again somewhere they can only
have the rest of it, and charge the new court's rate for minutes already played
on the old one.

So a move takes the whole hours **still ahead** of it. A booking of one to four,
moved at ten past two, moves the three o'clock.

This is also what makes the quote answerable at all. Availability marks an hour
that has begun as gone — correctly, because it cannot be sold — so an hour in
progress would come back as taken and refuse the whole move as "one of those
hours has just been taken". Leaving it behind means every hour that reaches the
pricing is one that has not started, which is exactly what availability is
willing to talk about.

Nothing changes for a booking that has not begun: none of its hours have
started, so none are held back, and the move is the one it always was.

### Moving each kind

**`Hourly`** — moves freely, including mid-session, and **keeps the same number
of hours**. A move changes when and where a booking is, never how much of it
there is; a screen that could add an hour by moving would be a way of buying one
without paying.

What is asked for depends on whether it has begun, and the two cases are worth
keeping apart:

* **Not begun.** The customer picks a date, then hours from a grid
  (`GET /bookings/{id}/move-window?date=`) which asks for exactly as many hours
  as the booking has, then a court. All three may change.
* **Under way.** Neither a date nor hours is asked for — **only a court**. The
  hours still ahead travel at the times they already have, and those times are
  not up for negotiation: the customer is in the middle of a session, and
  offering to reschedule the rest of it to Thursday is not what they came to the
  screen for. What gets priced is those remaining hours against the new court's
  rate, and the hour being played is not among them.

**`WholeDay` and `MultiDay`** — move by **date**, not by hour. Their hours were
never a choice anybody made; they are whatever the court is open for, so they
cannot be named until a court is. The client sends the dates it wants and each
candidate court is asked about its own days.

What they keep is **the number of days**, not the number of hours. A Tuesday is
under no obligation to be as long as the Saturday it replaces, and held to an
equal hour count, a whole-day booking could only ever move to a day of exactly
the same length — which on most rate cards means it could not move at all.

What keeps that honest is **the price rather than the count**: a longer or
dearer day comes out as a balance due, and a move that costs more does not go
through. It becomes an upgrade the customer is asked to pay for and the venue is
asked to accept. Nobody is handed hours they have not paid for, which is the
thing the count was protecting.

**And once the day has begun, a day booking does not move at all.** An hourly
booking under way still has whole hours ahead of it, and carrying those to
another court is the most useful thing a move does. A day taken open to close
has no such remainder to offer: moving it at noon would leave a customer with a
morning on one court and an afternoon on another, which is not the thing they
bought — so the day it is on is the day it stays on, and the button that offers
otherwise is not shown. `HasBegunByTheDay` answers this, and it is false for an
hourly booking whatever the clock says.

### What a move costs

**Court rental on both sides, platform fee excluded.** The fee is charged per
hour booked and a move buys no hours — the same number of them end up somewhere
else — so counting it would refuse a move between two courts that cost exactly
the same.

*Cheaper or equal:* the move goes through, and **nothing is refunded**. A
cheaper court is not a refund, which is the rule the booking policy states and
the move screen repeats.

*Dearer:* refused with `MoveCostsMore`. The move happens the moment it is asked
for, so there is no step left where money could change hands; letting it through
would hand the customer better hours and hand the venue the bill, without either
of them being asked. That case is an **upgrade**.

### Finding somewhere to move to

`POST /bookings/{id}/move-options` returns every court the booking could go to,
each already quoted. Every reason a court cannot take it — shut that day, closed
for work, already spoken for, never priced — arrives as a refused quote, and a
refused quote is why it is not on the list. The rules live in one place and the
search reads them rather than restating them.

Ordered **cheapest first**, so the free moves lead and the ones wanting paying
for follow. A list ordered by court name puts a bill at the top of the screen
for no reason the reader can see.

The booking's own court at its own hours is left off: that is not a move, and a
card whose only outcome is a refusal should not be offered.

---

## Upgrading a booking

`POST /api/v1/bookings/{id}/upgrade`. A customer asking to move onto hours that
cost **more** than the ones they hold, and offering to pay the difference.

This exists for the one case that cannot be instant: money has to change hands,
and the venue has to see it arrive before it gives up the better court. **Only
upgrades come through here** — a move to the same price or cheaper does not
create one at all.

### The flow

`AwaitingPayment` → `AwaitingApproval` → `Approved`, or `Declined`, `Withdrawn`,
`Expired`.

1. The customer asks. The request **holds the hours it is asking for**, on the
   same clock a booking's own hold uses.
2. The customer uploads a receipt for the balance and submits it.
   `AwaitingApproval`.
3. The desk approves or declines it.

An unpaid request lets go when its clock runs out, the same way an unpaid
booking does. Once the receipt is in, it holds the hours until the venue answers
— the customer cannot be blamed for a queue.

### The rules

* **The same move limit.** An upgrade is still a move, and paying for one must
  not be a way around the venue's figure.
* **One open request at a time** (`MoveAlreadyRequested`). Two, and the customer
  can be paying for hours while the venue is approving different ones.
* **Everything a move must satisfy**, because the quote is the same one: same
  sport, same venue, confirmed, shape kept, hours open, day not begun.
* **A reason**, the same as a free move's (see [why it moved](#why-it-moved)).
  Asked on the checkout before the request is sent, kept on the request, and
  counted as a move on the day the desk approves it.
* **There must be something to pay** (`NothingToUpgrade`). A free move is
  immediate, and sending somebody to a checkout for nought pesos is a step whose
  only effect is to make them wonder what they are being charged for.

### What is owed

`BalanceDue = RentalNew − RentalNow`, never less than nothing, **court rental on
both sides**. The platform fee is charged per hour booked and an upgrade buys no
hours, so counting it would put a price on a move that costs nothing.

**Fixed when the request is made**, not worked out again when it is paid. A rate
the venue changes in between must not change what somebody has already been
asked for. The hours themselves are copied onto the request at the price they
were quoted, rather than pointing at the booking's own slots — these are hours
the booking does not hold yet, and may never hold.

The figures stored are those of **the hours that are moving**, not of the whole
booking. The difference between the two is the hours already played, which sit
on both sides and cancel, so the balance is the same figure either way. What
changes is what the checkout can put on the page: a customer moving the last
hour of a long session is being asked for the difference on that hour, and a
receipt that opens with the total of a session mostly behind them is a receipt
for something else.

### At the desk

`POST /api/v1/desk/upgrades/{id}/approve` or `/decline`.

Approval re-checks rather than trusting the quote, because time has passed
since:

* the request is still `AwaitingApproval` — two people at one desk, and the
  first press stands (`UpgradeNotWaiting`)
* a receipt is attached (`NoReceipt`)
* the hours still add up: the hours already played plus the hours being bought
  must still be the whole booking, or the upgrade is stale (`UpgradeStale`). If
  the hour being paid for has itself begun on the old court since the customer
  asked, the sums really have stopped adding up.
* the target hours are still free (`UpgradeHoursTaken`)

Then the booking moves, **at the price it was quoted**, and `Booking.Settle`
records the balance against `PaidTotal`. A decline leaves the booking exactly
where it was, with the reason, and the customer is told.

`PaidTotal` is stored rather than derived, which the rest of `Booking` avoids on
principle. A move to a cheaper court leaves the slots totalling less than was
paid, and a move to a dearer one more; reading the money off the slots after
that would quietly restate a month that has already been billed. **The slots say
what is being played; `PaidTotal` says what was paid.**

---

## What the booking can tell a screen

`BookingDetail` carries `movesLeft` and `canBeMoved`, so a page can offer the
button or explain its absence without working the rules out again. `canBeMoved`
is answered **on the server**, because it is measured on the venue's clock, not
the reader's. See [Time Zone Rules](api-design.md#time-zone-rules).

`GET /bookings/{id}/history` reads the booking's own account of itself out of
the platform's audit trail — the same account the desk reads, because two
readers of one booking who disagreed about what happened to it would be worse
than either of them being wrong. Authorisation is the caller's: the customer's
screen asks whether the booking is theirs, the desk asks whether it is at a
venue they work at, and what comes back is the same.

---

## Not built

**A short human reference.** A customer at the counter can only quote a UUID.

**A background sweep for expired holds.** Lapsing is derived and correct without
one; a job would only be writing down what was already true, for tidier
reporting.

**Cancellation with money back.** There is none, by design. Moving is the answer
offered instead, and the platform never holds the money a refund would return.
