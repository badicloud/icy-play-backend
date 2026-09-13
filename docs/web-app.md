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
Catalogue       filter chips, activity cards, the court listing
Features        three cards
Footer          partner call to action
```

**The catalogue is read from the API, never written into the page.** A
hard-coded list promises sports nobody has a court for — a promise the search is
then obliged to break.

Three filters: **All**, **Sports**, **Events**, each with a count. The activity
cards **narrow the listing rather than unlock it**: every court shows by
default, and picking a sport filters. A visitor should see what is available
before being asked to choose.

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

**Details** — the space, booking rules, opening hours, and a panel for dividing
the court. That panel saves through its own endpoint: re-marking a floor is a
small, frequent change, and routing it through the whole court would put every
other field at risk to move one number.

**Pricing** — four rates per sport, each sport collapsible and independent so
two can be compared side by side. A bulk panel fills every sport at once, and
seeds itself from what is saved when every sport already charges the same. When
they differ it stays blank and says so, because filling it with one sport's rate
would misdescribe the others and the next Apply would overwrite them.

The peak window appears the moment a peak rate is typed, and is bounded by the
court's opening hours. Times are picked on a clock rather than in a native time
input, which is a different shape in every browser and unusable in some.

**Bookings** — not built, and says so. An admin who finds an empty panel cannot
tell whether the feature is missing or their court is.

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

**One read per page.** The court detail page is a single call. A page assembled
from several shows several different moments.

**Caching is cleared, not waited out.** The public catalogue is cached on both
the server and the client, and the server clears its copy on every admin write
that could change the answer. See
[courts-and-pricing.md](courts-and-pricing.md#caching).

---

## Not built

* **Booking.** No availability, no checkout, no confirmation. The customer-side
  half of [platform-fee-strategy.md](platform-fee-strategy.md) belongs with it.
* **The facility owner's own console.** Everything built is the platform admin
  acting on an owner's behalf. The `RoleGuard` and `app/(facility-owner)` route
  group are not built.
* **Google Maps Places Autocomplete.** `NEXT_PUBLIC_MAP_KEY` is empty, so
  addresses are typed and coordinates pasted. Directions links need no key and
  do work.
