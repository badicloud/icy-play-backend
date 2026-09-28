# Web App

## Purpose

The screens, what each is for, and the decisions behind them.

The code lives in the `icy-play-frontend` repository; this document lives here
because every other document does, and a reader looking for how IcyPlay works
should not have to know which repository to open. If the frontend grows its own
`docs` folder, this belongs there instead.

Related: [courts-and-pricing.md](courts-and-pricing.md),
[api-design.md](api-design.md).

---

## The public site

### Shared shell

`PublicHeader` and `PublicFooter` are on every public page. Extracted from the
landing page the moment a second page appeared: a page without them reads as a
different site, which is exactly what a visitor should not wonder halfway
through booking.

The footer carries one thing above everything else — **Become a Partner**. A
venue reading that far is the reader most worth asking. Its links go only where
something exists; a footer full of plausible headings that lead nowhere costs
more trust than a tidy grid buys.

### Landing page — `/`

```text
Hero            Find. Book. Play. Any Court, Any Event.
Venue catalogue one card per venue, with what it has and where it is
Features        three cards
Footer          partner call to action
```

**The landing page lists venues, not sports** (`FacilityCatalog`). It opened on
sports, which asked a visitor to name a game before it would tell them where
anybody plays — and a visitor who wants to play somewhere near them has a place
in mind long before a sport. Each venue then has a page of its own at
`/facilities/{slug}`, listing its courts.

**The catalogue is read from the API, never written into the page.** A
hard-coded list promises sports nobody has a court for — a promise the search is
then obliged to break.

A venue card leads with its **photograph**, because a venue is somewhere you go
and the decision is partly "would I want to spend two hours there", which no
list of sports answers. Under it: how many **bookable** courts — not courts, or
a venue with one hall marked out three ways would read as having nine — the
sports it runs as chips with their own artwork and a count each, and the address
as a link to Google's directions.

### Venue page — `/facilities/{slug}`

Where the sport is chosen, and where a booking starts (`FacilityCourts`). The
activity filter — **All**, **Sports**, **Events** — is narrowed to **only the
sports this venue actually has**: offering the full catalogue here would list
games nobody at this address plays, and every one of them would come back empty.

The chips **narrow the listing rather than unlock it**: every court shows by
default, and picking a sport filters. A visitor should see what is available
before being asked to choose. The chosen tab and sport survive in the address
bar, so a shared link and a back button both land where the reader was.

Empty states say *which* side is empty. "No venues are taking event bookings
yet" and "No courts are listed yet" mean different things — one says the
platform is new, the other that events have not been set up.

An activity an admin adds later with no artwork falls back to a letter in a
circle rather than a broken image.

### Court listing

One card per **bookable offering**: a court, or one marked-out part of one. A
floor divided three ways appears three times.

Each card carries a **sport badge with its icon**. This is not decoration: a
court set up for three sports appears three times at three prices, and without
the badge the rows read as duplicates.

The rates are laid out as a rate card rather than as chips — chips floating
above a divider read as decoration, and the rates are what a customer is
comparing. Only rates that **differ from the standard** are listed; a blank
means "same as standard", so showing an equal weekend rate would invent a
distinction the venue did not make.

The peak row carries its window in amber. "₱600 at peak" is not something a
customer can act on without knowing when peak is.

### Booking — `/book/{bookableCourtId}`

```text
Header
Booking type          by the hour · whole day · several days
Day strip             a fortnight ahead
Hour grid             one tile an hour, with its own price
Rates panel                                           (sidebar)
Floating summary      what is picked, what it costs, Continue
Footer
```

The route takes a **bookable court**, not a court: the id the catalogue already
hands out identifies the court, the sport and the part of the floor in one, so
the page never has to reassemble it from three query parameters.

The tick is the whole interaction, so it is a 30px target with a heavy stroke —
obvious at arm's length whether it is on, and tapping again takes it off.

**Nothing is priced in the browser.** Each hour arrives from the server with its
own rate and the platform's fee already worked out; the page adds them up. A
total the page computed itself is a total that can disagree with the bill.

Whole-day and several-day booking lock the grid and take every open hour. A day
with an hour already gone cannot be sold whole, and the page says so **before**
the customer commits rather than letting the server refuse them afterwards.

### Court detail — `/courts/{courtId}?sport=&division=`

```text
Header
Photo carousel        court photos first, then the venue's
Collapsible groups    the court · opening hours · amenities · rules · about
Pricing sidebar       rates, slot rules, Book now          (sticky)
Footer
```

The carousel shows one picture at a time at the size it was uploaded for. A grid
of twelve thumbnails shows everything and lets the reader see nothing. Nothing
is cropped — a blurred copy of the same picture fills the frame, so a portrait
photo of a court keeps the court in it.

Groups are shut by default except **The court** and **Opening hours**. A page
that opens everything at once is a wall; one that opens nothing makes the reader
work for the obvious.

The pricing panel is **sticky and outside the collapsible groups**, because the
price is what the reader checks against every detail they open below it.

### A court that is closed

A closure is the one thing on a card that changes what the reader can do, so it
is said three times over and never buried:

* an amber badge on the photo,
* a note in the card body carrying **when it reopens**,
* **Book now** replaced by **Under maintenance**.

The detail page still opens, and says the same thing at the top with a link back
to what else is available. A closed court is still worth reading about — a
customer deciding where to play next month wants the rates and the address, and
a page that refuses to load teaches them the venue is gone rather than shut.

**The admin's reason is not shown.** It is written for the audit trail, and
"owner has not paid" is a real thing to write there and the wrong thing to show
a customer. Customers get what they can act on: that it is shut, whether the
whole venue is down, and when it is back.

One function builds every version of that sentence. A card has room for a line
and a page for two, and copy written twice drifts apart.

---

## The customer's own bookings

### My bookings — `/bookings` and `/bookings/{bookingId}`

The list is cards; the detail page is the whole booking, its history, and
whatever it can still be offered. A card **caps the hours it lists** — a run of
days is hundreds of them, and a card that grows to fit is a list that cannot be
scanned.

The card says when a move is **waiting on the venue**, so somebody who has paid
an upgrade and is waiting is not left wondering whether the page forgot. And the
way out — cancelling — is offered **only once the booking is out of the
customer's hands**: while it is still an unpaid hold there is nothing to cancel
that letting the clock run out would not do.

### Moving — `MoveBookingDialog`

**The question is asked in the order a customer can actually answer it: when,
then which court.** It ran the other way round first — pick a court, then read
that court's diary — which meant choosing between courts before knowing which of
them could take you, and reading "that hour has gone" one court at a time. Now
the date and the hours come first, and the courts that can take them are what
comes back. A court that is shut, closed for work or already spoken for never
appears, because a card that cannot be clicked is a question the reader has to
answer twice.

What the dialog asks depends on the booking, and **the server decides which**:

| The booking | What is asked |
| --- | --- |
| `Hourly`, not begun | A date, then hours from the building's opening times, then the courts free for them |
| `Hourly`, under way | Nothing. The whole hours still ahead travel at the times they have; the hour being played stays where it is |
| `WholeDay`, not begun | A date, and no hours — a day's hours are whatever each court is open for, so the server works them out per court |
| `MultiDay`, not begun | As many dates as the run has, each picked and unpicked on its own. Once they are all chosen the rest of the strip goes quiet |
| Sold by the day, begun | Nothing is offered at all |

The booking's **own court is in the list**, because moving to other hours on the
same court is an ordinary thing to want. What is left off is that court at those
same hours, which is not a move.

Options arrive **cheapest first**, and a court that costs more carries its
balance on the card. Picking one goes to the upgrade checkout rather than moving
anything. A free one is **sent to the venue** — the button says *Send to the
venue*, and a snackbar says the booking stays where it is until they approve.
The panel at the top says how many moves are left, that only approved ones
count, and how many days before the start moves close.

The booking card says why the button has gone when the venue's notice has
closed moves (`isInsideMoveNotice`), rather than simply not showing it.

**Why are you moving it?** — `MoveReasonPicker`, shared by the dialog and the
upgrade checkout. The reasons are chips; a note box opens once one is picked,
optional except on Other. *Send to the venue* and *Pay and proceed* stay grey until there
is an answer the server will take. On an upgrade it is asked **on the checkout,
not in the dialog**, because the dialog hands over to the checkout through its
address and a customer's own words do not belong in a URL.

### Upgrade checkout — `/bookings/{bookingId}/upgrade`

The same shape as the booking checkout, because it is the same act: a balance to
pay, a GCash number to pay it into, a countdown on the hold, a receipt to
upload, and then a wait on the venue. The **extra confirm step was dropped** —
the customer has already chosen the court and seen the figure, and a second page
asking whether they meant it is a page that only adds doubt.

The receipt is capped at 10 MB: a receipt is a screenshot, and anything that
size is something else.

## The venue's desk

`/desk` — the booking queue (`Waiting` and `Confirmed`), the court diary, and
`/desk/upgrades` (*Move requests*), which is the same two-pile shape: what is
waiting is work and what is settled is a record. Every move lands there; each
row is badged **Move** or **Upgrade**, and only one with money owed is called an
upgrade, on the row, the overview banner and the decline dialog. Declining
takes a reason, which the customer is emailed.

`/desk/settings` holds the hold length, the move limit and the move notice (in
days), with a **change history** underneath: who changed which dial, from what
to what, and an *IcyPlay admin* badge when the platform set it. The admin sets
the same two move dials from the facility owner page (*Booking moves*), and the
owner page's Activity timeline names them.

A booking's **history** is readable from both the desk and the customer's own
page, and it is the same account of the same events.

**The bell.** For an owner or an attendant, the account button in the header
carries a bell with a red count while anything is waiting on the desk:
payments to check plus move requests to answer, across every venue they work. It
shakes, rests and shakes again, and holds still for anybody who has asked for
less motion. The menu under it has a row for each, linking to its queue. It
reads the same two counts as the desk overview, from the same cached queries,
polls every minute, and clears the moment the desk confirms or approves
something. It is absent when nothing is waiting, and a customer's header never
asks.

### Reports — `/desk/reports`

A side menu of every report, grouped by what it is about — courts and
bookings, sales, inventory — each group on its own card. The unbuilt ones stay
on it, greyed with a lock, so an owner looking for takings can see they are
coming rather than wonder whether they are in the wrong place. One list,
`reports.ts`, feeds the menu.

The menu belongs to the reports section, not to `DeskShell`, which has none on
purpose. The desk's pages are separate jobs and a breadcrumb says where you
are; reports are siblings read one after another.

**The landing page is "right now"**: courts, bookable courts, and how many of
the parts are available, booked or under maintenance at this minute. The last
three add up to the bookable courts, and the page says **available is not the
same as sellable** — a part with no booking of its own is still unsellable
while a clashing game has the floor.

**Court utilisation** — `/desk/reports/utilization`. A court per row, opened to
the parts it is sold in. The court's percentage counts the floor; the rows
count what was sold on it, and on a divided floor those are different numbers,
which the page says in as many words. Shares use the largest-remainder method so
the column totals 100, with a total row saying what it is a share of.

**Sold Hours** — `/desk/reports/hours`. The same figures spread over their
days, weeks or months, as a chart or a table, for the whole venue or per court.
The line has its own scale, because against the hours open it would lie flat on
the floor; what each period was — open, under maintenance, closed — is a strip
underneath. The line **breaks** on a closed period rather than dropping to zero:
nothing was on sale, which is not the same as nothing selling. Per court stops
at eight lines and hands over to the table, which has every court and exports
to CSV.

**Sold Courts** and **Not Sold Courts** — `/desk/reports/sold` and
`/desk/reports/unsold`. Two ends of one list, split by a filter on the
utilisation report rather than counted twice: a court is on exactly one of them,
and each page's footer says how many are on the other. Both show courts or
bookable courts.

**Every over-time report offers the same two views, Chart then Table**, with
**By** day, week or month in the filter bar — Sold Hours, Sold Courts and Not
Sold Courts alike. And every chart is a line: the first draft of these two had
bar charts, and three reports with three kinds of chart read as three products.

**The chart on both is one component, `CourtCountLine`.** How many courts — or
parts — had a booking each period (Sold Courts, in blue) or had none (Not Sold
Courts, in orange), beside a dashed line of how many were open. The same unit on
one axis, so the space between the lines is the other report, and the two
counted lines add up to the open one. A period the venue was closed is a gap in
both, not a zero: no courts for sale is not the same as every court selling.

**The table on Sold Courts** ranks busiest first. It carries a small trend line
per court, from the hours-over-time read so it is the same line Sold Hours
draws, and every row shares one vertical scale: scaled each to its own peak, a
court that sold one hour would draw the same mountain as one that sold twenty.
On bookable courts the total runs higher than on courts, because parts of one
floor sell the same hour, and a note says so in three lines before anybody
reports it.

**The table on Not Sold Courts** ranks longest-neglected first — never sold at
the top, then the oldest last sale. A court with a payment waiting on the desk
says so, so it is not written off while somebody's money is sitting on it.

**Moved Bookings** — `/desk/reports/moves`. How many bookings customers moved
and why, with the same filter bar, **By**, and Chart · Table. **Show** is *All
moves* — every move, with paid upgrades as a dashed line inside it — or *By
reason*, one line per reason in a fixed colour; *Not asked* appears only when
the range has a move from before customers were asked. Tiles for moved, paid
upgrades and the top reason; under the chart, the moves themselves, newest
first, with the customer, from → to, and the reason and note. The day only, not
the time: the day is the venue's and comes from the server, and a time worked
out in the browser would be the browser's clock.

**Declined Bookings** — `/desk/reports/declines`. The same shape. *All declines*
draws the refusals against a dashed line of every payment the desk checked, so
the gap is what it confirmed; *By reason* is one line per reason, with *Not
categorised* only when the range has a refusal from before the list. Tiles for
declined, the share of payments checked, and the top reason; under the chart,
each refusal with the customer, court and hours, amount, reason and note, and
who declined it. The chart is `CountChart`, shared with Moved Bookings.

**Takings** — `/desk/reports/takings`, in the Sales group. What customers paid,
on the day the desk confirmed it. **By** also offers Quarter, Half and Year.
*Whole venue* draws the takings against a dashed line of everything the
customers paid, so the gap is the platform fee; *Per court* is one line per
court, handing over to the table past eight. Tiles for what customers paid, the
platform fee, the venue's takings and what came from upgrades. The table has
bookings, hours, court rental, upgrades, platform fee and takings per period,
and per court with a subtotal; the CSV has every court and period. Open to
attendants until money has a permission of its own. `CountChart` takes a
`format` for its tooltip and axis, so the same chart draws pesos.

**Missed Income** — `/desk/reports/missed`, in the Sales group. What the open,
unsold hours would have earned at the venue's own rates, counting only hours that
have begun. The same filters as Takings, opening on By week. *Whole venue* draws
missed income against a dashed line of takings; *Per court* one line per court.
Tiles for missed income, hours not sold, what the empty peak hours were worth,
and missed income as a multiple of takings. Under the chart, every court most
missed first, opening to its sport courts — the court priced at its main sport,
each sport court at its own rate, with a note that the parts share one floor
and do not add up. Open to attendants until money has a permission of its own.

**Court Changes** — `/desk/reports/changes`, in the Inventory group. Every change
to the courts, newest first, grouped by day: an icon for the kind, the title,
the time and who, each line as before → after, and the reason. Chips filter by
kind with a count each; a Court picker narrows to one. Tiles for courts and
bookable courts now with what the range added and retired, price changes, and
maintenance closures with how many are on now. A list rather than a chart —
these are things that happened, not a figure that moves — with CSV export.

**Court Mix** — `/desk/reports/mix`, in the Inventory group. What the venue has
now: tiles for courts, how many are under a roof, lit, and set up for events; a
bar of indoor, covered and outdoor with each type's courts and share of open
hours sold; each sport and event with how many courts take it; and every court
with its venue type, surface, lighting, sports and events, bookable courts and
share sold. The dates only move the shares. **Show retired courts** lists them
greyed out with a badge, and never counts them.

**Attendants** — `/desk/attendants`, on the desk overview for owners only. The
admin console's own `AttendantsPanel`, once per venue, pointed at the owner's
desk (`OWN_DESK` in place of an owner id): add an attendant and they are emailed
the same activation link the admin's invitation sends, resend it, take them
off, and a **Can see money** checkbox on each, saved as it is ticked. An attendant without it is **not shown the money reports at all** — Takings,
Missed Income and Platform commission (`money: true` in `reports.ts`) are left
out of the menu, and the Sales group with them — rather than shown locked, since
a locked row says there is something they are not allowed. They also get no
rental on Court utilisation and a dash for the amount on Declined Bookings. The admin console's attendants panel shows a **Can see money** check
mark beside each attendant who has it.

**Turning a payment down** asks for a reason through `ReasonPicker` — the same
component the customer's move uses, so every place the platform asks why looks
and behaves alike.

## The admin console

### Dashboard — `/admin`

Grouped by what the reader came to do, not by when each page was built. A flat
list of seven makes the admin read all seven to find the one they want; a
heading lets them skip five.

| Group | Holds |
| --- | --- |
| Owners and venues | Facility owners, Facility inventory, Courts |
| What courts are booked for | Sports and events, Holidays |
| Accounts | Users |

**Onboarding is not in the groups.** It is an action rather than a place, so it
is the primary button in the header.

### Courts — `/admin/courts`

Every court on the platform, so an admin correcting one does not have to
remember which venue it is in. Filters for **facility owner** and **facility**,
both defaulting to All, plus a search across the court, the venue and the
business name — the three names an admin would actually type.

Choosing an owner resets the facility filter: a facility from the previous owner
would filter everything away.

Each row shows the bookable unit count, which is every division of every sport.
Saying "one court" for a floor marked out three ways would undersell the venue.

### Court page — `/admin/courts/{id}`

Three tabs.

**Details** — the space, booking rules, opening hours, a panel for dividing the
court, and below it the bookable courts themselves. The dividing panel saves
through its own endpoint: re-marking a floor is a small, frequent change, and
routing it through the whole court would put every other field at risk to move
one number.

The two panels are kept apart on purpose. The dividing panel says what a save
**will** do; the list below says what is **true now**, read from the server.
Folded together, an unsaved dropdown would read as a court somebody can book.

**Pricing** — four rates per sport, each sport collapsible and independent so
two can be compared side by side. A bulk panel fills every sport at once, and
seeds itself from what is saved when every sport already charges the same. When
they differ it stays blank and says so, because filling it with one sport's rate
would misdescribe the others and the next Apply would overwrite them.

The peak window appears the moment a peak rate is typed, and is bounded by the
court's opening hours. Times are picked on a clock rather than in a native time
input, which is a different shape in every browser and unusable in some.

**Bookings** — not built, and says so. An admin who finds an empty panel cannot
tell whether the feature is missing or their court is. The customer side of
booking is built; what is missing here is the venue's view of it, and the
attendant's queue for verifying payments.

### Reports — `/admin/reports`

The venue desk's reports, for the platform. The **same side menu** as the
desk's (`ReportsNav variant="admin"`), over the same list in `reports.ts`: a
report the admin does not have yet is greyed, and each one arrives by getting
an `adminHref`.

The landing page has a **Facility owner** filter and, once an owner is picked, a
**Venue** filter; both live in the address (`?owner=&venue=`) so the reports
built on it can carry them. Under them, **Right now** — the desk's five tiles
(`SnapshotTiles`, shared with the desk's own landing) for the whole platform,
or the owner or venue picked — and then **Per facility owner**, a row each with
the same five numbers small and an *Open reports* link that narrows the page to
them. The rows go when an owner is picked, because a list of one is the row
above it.

**Each report is the desk's own page.** The admin reports layout wraps its pages
in `AdminReportScope`, which reads the owner and venue from the address; a
report asks `useReportScope()` who is reading it and fetches from the desk or
the admin endpoint accordingly — the other query is switched off, not merely
ignored. `ReportFilters` shows the desk's venue picker or the admin's
`OwnerVenueFilter`, the breadcrumb comes from `reportTrail`, and the menu's links
carry `?owner=&venue=` so the scope follows the admin from report to report. A report
page asks for its data through `reportData.ts` (`useUtilizationReport`,
`useHoursReport`), which picks the console, and links to a sibling report with
`reportHref`, which keeps the scope. Every desk report is
now in the admin console under the same name (`/admin/reports/utilization`,
`hours`, `sold`, `unsold`, `moves`, `declines`, `takings`, `missed`, `changes`,
`mix`), with the rental in Court utilisation. Only Platform commission waits, on
billing. Court Changes picks its courts from the report's own list, retired
ones included, and starts again from every court when the admin changes owner
or venue.

### Other admin pages

| Page | Notes |
| --- | --- |
| `/admin/facility-owners` | The owner list. Each opens a detail page with business, contract terms, facilities and courts. |
| `/admin/facility-owners/{id}` | Contract terms carry **Edit dates** and **Change rates**. A start date typed wrong leaves an owner invisible to customers, which looks like a broken listing rather than a mistyped date — so the dialog says plainly whether the term covers today. |
| `/admin/facility-inventory` | Every venue and whether it can be booked today, with one actionable reason when it cannot. |
| `/admin/sports` | Sports and events. The dialog asks the kind **first**, because that decides which categories make sense. |
| `/admin/holidays` | The calendar, with a warning when a moving holiday's date has passed. |

---

## Conventions

**Edits ask why.** Every edit dialog carries a reason that lands in the audit
trail. The entry says what changed; only the person changing it can say why, and
a trail of what-without-why is half a trail.

**Nothing pretends to work.** A feature that is not built says so where it would
have been, rather than being hidden or faked. The Book now button is disabled
and labelled; the Bookings tab explains itself.

**Names are derived, not stored.** Court division names come from the court's
name, the sport and the number. Renaming the court renames them; a stored name
would survive the rename and lie.

**Counts are read, not recomputed.** How many courts a venue has is answered
once, by the `BookableCourts` table, and the filter chips, the listing, the
inventory and the court page all read it. Two screens counting the same thing
two different ways is how one of them comes to be wrong.

**One read per page.** The court detail page is a single call. A page assembled
from several shows several different moments.

**Caching is cleared, not waited out.** The public catalogue is cached on both
the server and the client, and the server clears its copy on every admin write
that could change the answer. See
[courts-and-pricing.md](courts-and-pricing.md#caching).

---

## Not built

* **A message thread.** A declined customer is emailed the venue's phone and
  email, because there is nowhere in IcyPlay to answer from. This is that
  missing place.
* **The facility owner's own console.** Everything built is the platform admin
  acting on an owner's behalf. The `RoleGuard` and `app/(facility-owner)` route
  group are not built.
* **Google Maps Places Autocomplete.** `NEXT_PUBLIC_MAP_KEY` is empty, so
  addresses are typed and coordinates pasted. Directions links need no key and
  do work.
