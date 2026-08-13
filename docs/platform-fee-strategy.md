# Platform Fee Strategy

## Purpose

This document defines how the Platform charges Facility Owners for using the booking system.

This is separate from court rental pricing.

Court rental pricing is configured by the Facility Owner for Customers.

Platform fee pricing is configured by the Platform for Facility Owners.

---

## Core Idea

The platform fee should be calculated per booked hour.

Example:

```text
Platform rate: 10 per hour
Booking duration: 2 hours
Platform fee: 20
```

The Customer still pays the full booking amount directly to the Facility Owner.

```text
Customer payable amount = Court rental amount + Platform fee
```

The Platform records the platform fee and later bills the Facility Owner based on the billing cycle.

---

## Why Per-Hour Platform Fee

Per-hour platform fee is a better fit for court booking because longer bookings create more value and usage.

Benefits:

* Fairer than a flat fee per booking
* Easier to explain to Facility Owners
* Works naturally with court duration
* Supports internal client-specific pricing
* Supports discounted ranges for longer bookings
* Keeps platform fee calculation traceable per booking

---

## Platform Fee Ownership

Platform Administrators manage platform fee agreements.

Facility Owners may view their own agreement but should not be able to change platform fee rules.

Responsibilities:

* Platform Admin configures rate per hour.
* Platform Admin configures pricing ranges.
* Platform Admin configures billing cycle.
* Platform Admin configures cashback percentage, if applicable.
* Facility Owner reviews billing reports and pays the Platform.

---

## Pricing Models

### Per-Hour Rate

The simplest model is a fixed platform fee per booked hour.

Example:

```text
Rate per hour: 10
Booking duration: 2 hours
Platform fee: 20
```

### Range-Based Pricing

Range-based pricing gives a lower total platform fee when the booking reaches a certain duration.

This is useful for encouraging longer bookings and giving better pricing to Facility Owners.

Example:

| Booking Duration | Normal Calculation | Discounted Platform Fee |
| --- | --- | --- |
| 1 hour | 10 | 10 |
| 2 hours | 20 | 20 |
| 4 hours | 40 | 35 |
| 10 hours | 100 | 90 |
| 1 day | 240 | 200 |

Rule:

* If a booking reaches a configured duration threshold, use the discounted platform fee for that range.

### Internal Client Pricing

Facility Owners are internal clients of the Platform.

The Platform should support different agreements per Facility Owner.

Examples:

```text
Facility Owner A: 10 per hour
Facility Owner B: 8 per hour
Facility Owner C: 10 per hour with discounted daily rate
```

This gives flexibility for negotiated pricing, early partners, high-volume owners, or special business relationships.

---

## Cashback Percentage

The Platform may provide cashback to Facility Owners per billing cycle.

This is a billing-level incentive, not a direct customer discount.

Purpose:

* Give Facility Owners a small incentive
* Support court maintenance allowance
* Support utilities or operational support
* Encourage continued platform usage
* Support partner-style agreements

Example:

```text
Gross platform fees for billing period: 1,000
Cashback percentage:                  5%
Cashback amount:                      50
Net amount due to Platform:           950
```

Recommended treatment:

* Customer still pays the normal total payable amount.
* Platform fee is recorded at booking time.
* Billing report shows gross platform fee.
* Billing report shows cashback percentage and cashback amount.
* Facility Owner pays the net amount after cashback.

This keeps the customer payment flow simple while giving the Platform flexibility in Facility Owner billing.

---

## Platform Fee Calculation Flow

```text
Customer selects court and schedule
  |
  v
Platform calculates booking duration
  |
  v
Platform loads active Facility Owner platform fee agreement
  |
  v
Platform applies per-hour rate
  |
  v
Platform checks range-based pricing
  |
  v
Platform calculates platform fee
  |
  v
Platform adds platform fee to customer payable amount
  |
  v
Booking stores calculated fee values
```

---

## Billing Calculation Flow

```text
Billing cycle closes
  |
  v
Platform totals billable platform fees
  |
  v
Platform calculates cashback, if configured
  |
  v
Platform generates billing report
  |
  v
Facility Owner reviews gross fee, cashback, and net amount due
  |
  v
Facility Owner pays Platform
```

---

## Stored Booking Values

Each booking should store the platform fee values calculated at booking time.

Store:

* Platform fee agreement ID
* Platform fee model
* Booking duration in minutes
* Rate per hour
* Applied range rule, if any
* Gross platform fee amount
* Total payable amount

This prevents historical bookings from changing when platform fee agreements are updated later.

---

## Stored Billing Values

Each billing record should store both gross and net billing values.

Store:

* Gross platform fee amount
* Cashback percentage
* Cashback amount
* Adjustments
* Net amount due

Example:

```text
Gross platform fee amount: 1,000
Cashback percentage:      5%
Cashback amount:          50
Adjustments:              0
Net amount due:           950
```

---

## MVP Recommendation

Start with:

* Per-hour platform fee
* Range-based discounted platform fee
* Facility Owner-specific agreement
* Billing cycle configuration
* Cashback percentage per billing cycle

Do not include:

* Automatic deduction from Facility Owner payment accounts
* Automatic online collection of platform fees
* Complex revenue sharing
* Customer-facing cashback

---

## Business Rules

* Platform fee is charged per booked hour by default.
* Platform fee agreement belongs to one Facility Owner.
* Platform Admin manages platform fee configuration.
* Facility Owner can view their platform fee agreement.
* Customer pays court rental amount plus platform fee directly to Facility Owner.
* Platform fee is stored at booking time.
* Historical platform fee amounts do not change when agreements are updated.
* Cashback is applied during billing, not during booking.
* Billing report must show gross platform fees, cashback, adjustments, and net amount due.

---

## Related Documents

* `business-model.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `booking-workflow.md`
* `database-design.md`
* `api-design.md`
* `roadmap.md`
