# Courts and Pricing

## Purpose

This document describes what a court is on IcyPlay, what it can be booked for,
how it is divided, and how it is priced.

It covers what has been built. Where a decision was taken that the earlier
strategy documents did not anticipate, that is called out rather than quietly
folded in.

Related: [pricing-strategy.md](pricing-strategy.md),
[platform-fee-strategy.md](platform-fee-strategy.md),
[database-design.md](database-design.md).

---

## The shape of it

```text
FacilityOwner
  └── Facility              address, hours, amenities, photos
        └── Court           the physical space, its booking rules, its photos
              └── CourtSport    one row per thing the court can be booked for
```

`CourtSport` is the pair everything commercial hangs off. It carries:

* which sport or event the court is set up for
* **how many playable courts** that makes
* **what it costs** — four rates

Both were put there rather than on the court, because both change per sport. A
floor is one basketball court and three pickleball courts, and an hour of each
is not worth the same.

---

## Sports and events

The lookup holds two kinds of thing, told apart by `Sports.Kind`:

| Kind | Meaning | Examples |
| --- | --- | --- |
| `Sport` | Something played on the court | Basketball, Badminton, Pickleball |
| `Event` | Something the floor is hired for | Birthday party, Tournament, Concert |

The distinction is **recorded, not inferred from the category**. A customer
browsing for a game must not be offered a wedding, and an admin who files a
tournament under "Court sports" should not break that.

Both kinds are priced and divided the same way, which is why they share a table.

Seeded with the common sports plus five events. Both lists are managed from
`/admin/sports` — a venue that hosts something nobody thought of should not
need a deploy.

---

## Dividing a court

`CourtSport.Divisions` says how many playable courts the floor makes when it is
set up for that sport. One means the court is played whole, which is the
ordinary case. The maximum is twelve: high enough for a hall marked out end to
end, low enough that a typo does not create a hundred bookable courts.

Division names are **derived, never stored**:

```text
Divisions = 1   ->  "Court 1"
Divisions = 3   ->  "Court 1 · Pickleball 1"
                    "Court 1 · Pickleball 2"
                    "Court 1 · Pickleball 3"
```

Renaming the court renames its divisions with it. A stored name would survive
the rename and lie.

A court played whole keeps its own name — numbering one of one only invites the
question of where the second is.

### The rule that is not built yet

**Divisions share a floor and therefore conflict.** If the whole court is booked
for basketball, all three pickleball courts are unavailable; if one pickleball
court is booked, the basketball booking cannot be taken.

Nothing enforces this today, because booking does not exist yet. It is the first
rule Phase 5 has to carry, and `CourtSport.Divisions` is the field it will be
answered from.

---

## Court pricing

Four rates per `CourtSport`, all `decimal(10,2)`:

| Rate | Meaning |
| --- | --- |
| `StandardHourlyRate` | An ordinary hour. Null means the sport is not priced. |
| `PeakHourlyRate` | Inside the court's peak window. |
| `WeekendRate` | Saturday and Sunday. |
| `HolidayRate` | A day on the holiday calendar. |

The three special rates are **nullable and fall back to the standard one**. A
venue charging the same all week stores one number rather than four copies of
it, and a blank means "same as standard" rather than "free".

`CourtSport.RateFor(kind)` answers the fallback in one place, so it cannot be
spelled differently at each call site.

A special rate may not be set without a standard one. It would be charged as the
standard anyway, which is not what typing it meant.

### The peak window

The peak *rate* is per sport. The peak *window* is on the **court**, because a
venue is busy at the same hours whatever is being played on it.

```text
Courts.PeakStartsAt      TIME
Courts.PeakEndsAt        TIME
Courts.PeakOnWeekdays    BIT
Courts.PeakOnWeekends    BIT
```

Rules:

* A peak rate cannot be set without a window. A rate with no window can never
  be charged.
* The window must fall **inside the court's opening hours**, measured at the
  narrowest across the days it applies to. A window that fits Monday but
  overruns an early Saturday close is wrong on the Saturday.
* At least one of weekdays or weekends must be ticked.
* When no sport on the court charges a peak rate, the window is cleared. A
  window nobody is charged for reads as a live rule the next time it is opened.

Because the window sits inside opening hours, it cannot wrap past midnight —
`Court.IsPeakAt` is a simple range, not a two-part test.

---

## Holidays

A managed table rather than a fixed list, because half the Philippine calendar
moves.

```text
Holidays
  Name
  Date               the day; for a repeating one only month and day are read
  Kind               Regular | Special non-working
  RepeatsAnnually    true for Christmas, false for Maundy Thursday
  IsActive
```

Seeded with the eleven fixed Philippine holidays. The movable ones — Maundy
Thursday, Good Friday, National Heroes Day, Eid'l Fitr, Eid'l Adha — are
**deliberately absent**: they land on a different date every year, and seeding
them as repeating would put them on the wrong day for every year after the
first.

`/admin/holidays` warns when a non-repeating holiday's date has passed and has
not been given a new one. Without that, it simply stops being a holiday and
nobody notices.

Retired rather than deleted: a booking priced as a holiday needs the day that
made it one to still be there when the receipt is questioned.

---

## What IcyPlay charges the owner

Held on the **contract term**, not on the owner. A rate changed on the owner
would rewrite what was agreed for terms already served.

```text
FacilityOwnerContracts.PlatformHourlyRate      decimal(10,2)  default 15.00
FacilityOwnerContracts.CommissionPercentage    decimal(5,2)   default  3.00
```

`FacilityOwnerContract.ChargesFor(bookedHours)`:

```text
50 hours booked in the period
50 × ₱15.00          = ₱750.00    billed to the facility owner
3% of ₱750.00        =  ₱22.50    maintenance and commission
                        ────────
the rest of the bill   ₱727.50
```

The commission is a percentage **of the bill**, not on top of it, and not of
what the customer paid.

Zero is allowed on both. A venue onboarded as a favour pays nothing, and
refusing to record that would only push it into a side agreement nobody can see.

### How this relates to the strategy documents

[platform-fee-strategy.md](platform-fee-strategy.md) already describes the
per-hour platform fee and the customer paying `court rental + platform fee`
directly to the owner, with the platform billing the owner afterwards. What is
implemented is that billing step: hours × rate.

**The commission percentage is new** and was not in that document. It is
recorded here and in
[platform-fee-strategy.md](platform-fee-strategy.md#maintenance-and-commission).

Nothing yet totals booked hours into a period or produces an invoice. The rates
and the arithmetic exist; the bookings to count do not.

---

## Bookable courts

A court is not the thing a customer books. **One floor, sold five ways** is the
ordinary case:

| Bookable court | Kind |
| --- | --- |
| Che court 1 | Whole — basketball |
| Che court 1 | Whole — volleyball |
| Che court 1 · Pickleball 1 | Divided |
| Che court 1 · Pickleball 2 | Divided |
| Che court 1 · Pickleball 3 | Divided |

`BookableCourts` holds one row each: the court-and-sport pair it belongs to, the
floor it is on, which part it is, and whether it is whole or divided.

### Why it is stored

Everything else here is derived — division names, owner status, maintenance,
rate fallbacks. This is not, and the reason is the booking.

A booking needs something that **cannot be recalculated out from under it**. Once
part three is sold, re-marking the floor into two has to be a conversation
rather than a subtraction. Nothing here deletes: a part that is no longer marked
out is retired, and marked out again it comes back as **the same row**, so a
booking that outlived the gap still resolves.

`Kind` is stored for a smaller reason that is easy to miss: **part one of three
and a court played whole both carry number one**. Once the count changes, or the
row is retired and the count moves on without it, nothing left on the row can
tell the two apart.

### One floor, one sport at a time

The five are five ways of selling one slab of concrete, not five resources.

| | Clashes? |
| --- | --- |
| Different floors | No |
| Same floor, different sport | **Yes** — a basketball game and a pickleball game cannot share the markings |
| Same floor, same sport, different part | No — three games at once is the point |
| Same floor, same sport, same part | **Yes** |

`BookableCourt.ConflictsWith` is four lines and is the only place this is
written down.

Which parts of a floor physically overlap is deliberately **not** modelled.
Three pickleball courts and two badminton courts marked on one basketball floor
do not line up, and a model claiming to know how would be wrong in a way nobody
could see until two games were sold the same paint.

### Who writes it

`BookableCourtRoster` — called from creating a court, editing one, and
re-marking the floor, in the caller's own `SaveChanges` so a court and what it
sells go in together or not at all. It writes only what differs, so every caller
calls it unconditionally rather than working out whether it needs to.

The `AddBookableCourts` migration backfills every court that already existed.
That SQL repeats in the database what the roster does in C#, which is the one
duplicate of the rule: deliberate, because a migration is frozen the moment it
runs and cannot drift forward, and an integration test holds every
court-and-sport pair in the database to the same invariant — one active part per
division, numbered from one without gaps.

### Where it is read

The public listing and the admin court page both read this table rather than
counting divisions. Counting could never come back empty; reading can, so a
court configured but missing its roster is **logged as a warning** rather than
quietly vanishing from the listing.

### Not built

Removing a sport from a court still deletes its bookable courts through the
foreign key. That is correct while no bookings exist and **must become a
refusal** when they do — the same conversation as narrowing a floor that has
bookings on its last part.

---

## The public catalogue

Two anonymous endpoints back the landing page.

`GET /api/v1/catalog/activities` — the sports and events that have a court
actually configured for them, with counts. An activity appears only when it has
an active court, in an active facility, whose owner has a contract covering
today. Maintenance is deliberately **not** counted: it is temporary, and
dropping a sport because one venue is resurfacing would hide every other venue
that has it.

`GET /api/v1/catalog/courts?sport=` — every bookable court, or those for one
sport. A divided floor is returned **part by part**, one row per division, each
carrying the sport it is for. Omit `sport` for everything on offer.

### Caching

Both are cached. Every visitor asks the same question and the answer changes
only when an admin configures something.

* Ten-minute absolute expiry, as a backstop.
* Cleared explicitly on every write that could change the answer: creating or
  updating a court, changing divisions, setting or lifting maintenance, any
  sport lookup change, and renewing, rescheduling or cancelling a contract.
* Maintenance counts, and it is easy to argue it does not. A closure is
  temporary and the activity list ignores it, but the **court list carries it**:
  whether the court is shut, when it reopens, and whether the whole venue is
  down. Leaving the cache alone kept a reopened court reading as closed for up
  to ten minutes. Two integration tests pin both directions.
* `CatalogCacheSignal` is one switch that evicts every cached key at once —
  the activity list and each sport's court list. Holding one while clearing the
  others is how a filter comes to offer a sport whose only court has just gone.
* `Cache-Control: public, max-age=60` so browsers help without holding an admin
  edit for long.

---

## Admin surfaces

| Page | What it is for |
| --- | --- |
| `/admin/courts` | Every court on the platform, filtered by owner or facility. Defaults to all. |
| `/admin/courts/new` | The court wizard: pick or add a facility, then the court. |
| `/admin/courts/[courtId]` | One court. Tabs: Details, Pricing, Bookings. |
| `/admin/sports` | The sports and events lookup. |
| `/admin/holidays` | The holiday calendar. |
| `/admin/facility-inventory` | Every venue, with its booking status. |

The court page's **Pricing** tab sets all four rates per sport, with a bulk fill
for the common case, and the peak window appears the moment a peak rate is
typed. The **Details** tab sets the divisions through its own endpoint:
re-marking a floor is a small, frequent change, and routing it through the whole
court would put every other field at risk to move one number.

**Bookings** is not built, and says so. An admin who finds an empty panel cannot
tell whether the feature is missing or their court is.
