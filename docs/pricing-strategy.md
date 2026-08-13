# Pricing Strategy

## Purpose

This document defines how Facility Owners can configure court rental pricing.

The goal is to let Facility Owners manage flexible pricing without changing the core booking and payment model.

The current business model remains:

```text
Customer pays Facility Owner directly.
Customer pays court rental amount plus platform fee.
Platform records the booking and bills the Facility Owner for the platform fee later.
```

---

## Pricing Goals

The pricing system should support real sports facility operations.

Facility Owners should be able to configure:

* Standard court rental fees
* Weekend rates
* Holiday rates
* Time-based rates
* Combination pricing
* Promotional pricing
* Package rates

The pricing system should also prepare the platform for future e-commerce features such as consumable items, equipment rentals, bundles, and add-ons.

---

## Pricing Ownership

Facility Owners own court rental pricing.

Responsibilities:

* Set base court rental fees
* Configure special rates
* Configure package rates
* Configure promotions
* Review pricing before publishing changes
* Honor confirmed booking prices

The Platform calculates the booking amount using the active pricing rules configured by the Facility Owner.

The Platform should not silently change prices after a booking is created.

---

## Pricing Components

### Base Rate

The base rate is the normal rental price for a court.

Example:

```text
Court A base rate: 300 per hour
```

This should be the fallback price when no special rule applies.

### Time-Based Rate

Facility Owners may charge different prices depending on time of day.

Example:

```text
8:00 AM - 5:00 PM: 250 per hour
5:00 PM - 10:00 PM: 350 per hour
```

This is useful for peak and off-peak pricing.

### Weekend Rate

Facility Owners may charge a different price for Saturdays and Sundays.

Example:

```text
Weekday rate: 300 per hour
Weekend rate: 400 per hour
```

### Holiday Rate

Facility Owners may charge special rates during holidays or manually selected dates.

Example:

```text
Holiday rate: 450 per hour
```

Holiday pricing should be date-based and configured by the Facility Owner.

### Combination Pricing

Combination pricing allows multiple rules to work together.

Example:

```text
Court: Court A
Day: Saturday
Time: 6:00 PM - 8:00 PM
Applicable rules:
  Base rate
  Weekend rate
  Peak hour rate
```

The system must have a clear rule for which price wins.

Recommended approach:

* Use priority-based pricing rules.
* The most specific active rule wins.
* Store the final calculated price on the booking.

Example priority:

| Priority | Rule Type |
| --- | --- |
| 1 | Package rate |
| 2 | Promotion |
| 3 | Holiday rate |
| 4 | Weekend rate |
| 5 | Time-based rate |
| 6 | Base rate |

This means a holiday rate can override a weekend rate, and a package rate can override normal hourly pricing.

### Promotional Pricing

Promotions allow Facility Owners to temporarily discount court rental prices.

Examples:

* Opening promo
* Morning discount
* Weekday discount
* First-time customer promo
* Limited date promo

Promotion configuration may include:

* Promo name
* Discount type
* Discount amount or percentage
* Start date
* End date
* Applicable courts
* Applicable days
* Applicable time range
* Usage limit, future

MVP recommendation:

* Start with manual fixed discount or percentage discount.
* Apply only to selected courts and date ranges.

### Package Rate

Package rates allow Facility Owners to sell a bundled court rental arrangement.

Examples:

```text
2-hour badminton package: 550
3-hour pickleball package: 800
Morning court package: 700
Team practice package: 1,200
```

Package configuration may include:

* Package name
* Court or sport type
* Duration
* Package price
* Valid days
* Valid time range
* Valid date range
* Included items, future

For MVP, package rates should focus on court rental duration only.

---

## Pricing Calculation Flow

```text
Customer selects court
  |
  v
Customer selects date and time
  |
  v
Platform loads active pricing rules
  |
  v
Platform determines applicable pricing rule
  |
  v
Platform calculates court rental amount
  |
  v
Platform adds configured platform fee
  |
  v
Customer sees total payable amount
```

Example:

```text
Court rental amount: 400
Booking duration:    2 hours
Platform rate:       10 per hour
Platform fee:        20
Customer pays:       420
```

The Customer pays the full amount directly to the Facility Owner.

---

## Price Locking

The booking should store the calculated price at the time of booking.

Store:

* Court rental amount
* Applied pricing rule ID
* Applied pricing rule name
* Platform fee amount
* Total payable amount

This is important because Facility Owners may change prices later.

Existing bookings should not change when pricing rules are updated.

---

## Pricing Rule Conflicts

Pricing rules may overlap.

Examples:

* Weekend rate and holiday rate both apply.
* Promotion and package rate both apply.
* Two promotions apply to the same court and time.

Rules:

* The system must prevent unclear pricing.
* Each pricing rule should have a priority.
* If two rules have the same priority and overlap, the system should reject the configuration or require Facility Owner confirmation.
* The booking should show which rule was applied.

---

## MVP Pricing Recommendation

For the Minimum Viable Product (MVP), start with:

* Base court rental rate
* Weekend rate
* Holiday rate
* Time-based rate
* Basic package rate
* Basic promotion

Keep advanced rules for later:

* Customer-specific pricing
* Membership pricing
* Coupon codes
* Usage-limited promotions
* Multi-court package pricing
* Consumable item bundles
* Equipment rental bundles

---

## Future E-Commerce Direction

The pricing strategy should prepare the system for future e-commerce features.

Possible future add-ons:

* Bottled water
* Sports drinks
* Shuttlecock
* Pickleball racket rental
* Ball rental
* Towel rental
* Court equipment rental
* Merchandise

Possible future bundles:

```text
Court rental + bottled water
Court rental + racket rental
Court rental + ball rental
Court rental + team package
```

Future e-commerce should be documented separately before implementation.

Important future rule:

* Court rental pricing and item pricing should be related but not mixed too early.

For now, the MVP should focus on court rental pricing only.

---

## Reporting Considerations

Pricing reports should help Facility Owners understand performance.

Useful reports:

* Bookings by pricing rule
* Revenue by court
* Revenue by package
* Promotion usage
* Weekend vs weekday bookings
* Peak hour revenue
* Holiday booking performance

Dapper is a good fit for these reports because pricing analytics may require grouping, aggregation, and date-based filtering.

---

## Business Rules

* Facility Owner manages court rental pricing.
* Platform calculates prices from active Facility Owner pricing rules.
* Customer sees court rental amount, platform fee, and total payable amount before paying.
* Customer pays the total amount directly to the Facility Owner.
* Booking stores the calculated price at booking time.
* Existing booking prices do not change when pricing rules are updated.
* Pricing rules should have clear priority.
* Overlapping rules should be prevented or resolved by priority.
* E-commerce items are future scope, not MVP scope.

---

## Related Documents

* `business-model.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `database-design.md`
* `api-design.md`
* `roadmap.md`
