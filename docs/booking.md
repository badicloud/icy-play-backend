# Booking

## Purpose

How an hour of a court is sold: what is free, what it costs, and what taking it
does. Related: [courts-and-pricing.md](courts-and-pricing.md),
[platform-fee-strategy.md](platform-fee-strategy.md).

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

---

## Taking it

`POST /api/v1/bookings`, signed in as a customer. The client sends the hours it
wants and which **kind** of booking it is claiming, and the server checks the
claim against the hours:

| Kind | Means |
| --- | --- |
| `Hourly` | Any hours on one date. They need not run back to back. |
| `WholeDay` | Every open hour of one date. |
| `MultiDay` | Every open hour of each date in a consecutive run. |

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

---

## Not built

**Paying, and everything that hangs off it.** A booking is created
`PendingPayment` and stays there. The intended flow, for the MVP:

1. the customer pays by **GCash**, to the facility owner's number or QR code
2. the booking holds the court **for a configured number of minutes** and
   expires if payment does not arrive
3. the customer **uploads the GCash receipt**
4. a **facility attendant or platform admin verifies it**, which confirms the
   booking

None of that exists yet. What that means today, and it is the part worth
knowing: **a held booking never expires**, so a `PendingPayment` booking holds
its hours indefinitely without anyone paying. Nothing stops one account from
holding every hour of every court. Acceptable while the platform is not open to
the public; the expiry is the first thing to build when it is.

Also missing: the venue is not notified that a booking exists, there is no
console for an attendant to work through, and a booking has no short human
reference — a customer at the counter can only quote a UUID.
