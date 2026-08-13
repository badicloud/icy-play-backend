# Business Model

## Purpose

This document defines how money, responsibility, platform fees, and billing move across the Sports Facility Booking & Management Platform.

The simplified business model is:

```text
Customer pays Facility Owner directly.
Facility Owner verifies payment.
Platform tracks platform fee.
Platform bills Facility Owner later.
```

The Platform records and reports booking activity but does not receive customer booking payments directly.

---

## Core Model

The Platform is a SaaS product used by Facility Owners to manage facilities, courts, bookings, payment receipt verification, reports, and platform fee billing.

The Facility Owner is the owner/operator of the court. Each Facility Owner configures their own payment QR Code or payment instructions.

The Customer pays the full booking amount directly to the Facility Owner.

The full booking amount is:

```text
Court rental amount + Platform fee
```

The court rental amount is calculated from Facility Owner-managed pricing rules such as base rates, weekend rates, holiday rates, promotions, and package rates.

Example:

```text
Court rental amount: 300
Booking duration:    2 hours
Platform rate:       10 per hour
Platform fee:        20
Customer pays:       320
```

The Facility Owner receives the full customer payment. The Platform later bills the Facility Owner for the accumulated platform fees.

---

## Money Flow

### Customer to Facility Owner

The Customer pays the Facility Owner directly using the Facility Owner's configured payment method.

Examples:

* GCash QR Code
* Maya QR Code
* Bank transfer instructions
* Other Facility Owner-managed payment channels

The Platform only displays the Facility Owner's payment details and calculates the amount to pay. It does not collect, process, escrow, settle, or refund the customer's payment.

### Platform Fee

The platform fee is the amount earned by the Platform from confirmed bookings.

The platform fee can be configured depending on the agreement with the Facility Owner.

Possible platform fee models:

* Per-hour fee
* Range-based platform fee pricing
* Discounted daily platform fee
* Custom agreement per Facility Owner
* Cashback percentage per billing cycle

MVP recommendation:

* Start with a configurable per-hour platform fee.
* Support range-based pricing for longer bookings.
* Support cashback percentage during billing.

Example:

```text
Rate per hour: 10

Booking A duration: 1 hour
Booking A platform fee: 10

Booking B duration: 4 hours
Normal platform fee: 40
Discounted range fee: 35

Booking C duration: 1 day
Normal platform fee: 240
Discounted daily fee: 200

Gross platform fee due: 245
Cashback percentage: 5%
Cashback amount: 12.25
Net amount due: 232.75
```

### Platform to Facility Owner Billing

The Platform bills Facility Owners for accumulated platform fees.

Billing may be based on:

* Daily cycle
* Weekly cycle
* Monthly cycle
* Specific date range
* Custom agreement

The Platform provides the billing report. The Facility Owner is responsible for paying the Platform based on that report.

---

## Role Responsibilities

### Customer

The Customer is responsible for:

* Selecting a court and schedule
* Paying the Facility Owner directly
* Paying court rental amount plus platform fee
* Uploading a valid payment receipt
* Waiting for Facility Owner verification

The Customer does not pay the Platform separately for the platform fee.

### Facility Owner

The Facility Owner is responsible for:

* Managing facilities
* Managing courts
* Configuring court pricing
* Configuring platform fee agreement with the Platform
* Configuring payment QR Code or payment instructions
* Receiving customer payments directly
* Checking uploaded payment receipts
* Confirming valid bookings
* Rejecting invalid or unpaid bookings
* Reviewing platform fee billing reports
* Paying the Platform based on billing cycle

The Facility Owner owns customer payment verification.

### Platform

The Platform is responsible for:

* Recording booking activity
* Calculating the customer payable amount
* Calculating platform fee per booking
* Storing payment receipt uploads and metadata
* Generating booking reports
* Generating platform fee billing reports
* Sending billing reports to Facility Owners
* Tracking billing cycles and payment status

The Platform never receives customer booking payments directly.

---

## Booking Revenue Flow

```text
Customer creates booking
  |
  v
Platform calculates rental amount + platform fee
  |
  v
Platform shows Facility Owner payment details
  |
  v
Customer pays Facility Owner outside the Platform
  |
  v
Customer uploads receipt
  |
  v
Facility Owner verifies receipt
  |
  v
Booking becomes Confirmed
  |
  v
Confirmed booking appears in reports
  |
  v
Platform fee is added to Facility Owner billing
```

Only confirmed bookings should be used for platform fee billing unless a specific report explicitly includes pending, rejected, or cancelled bookings.

---

## Billing Flow

```text
Platform records confirmed bookings
  |
  v
Platform sums platform fees by billing cycle
  |
  v
Platform generates billing report
  |
  v
Platform sends billing report to Facility Owner
  |
  v
Facility Owner reviews billing report
  |
  v
Facility Owner pays Platform
```

Billing reports should be traceable to booking records, courts, facilities, Facility Owners, platform fee configuration, and billing periods.

---

## Billing Periods

Billing periods should be configurable per Facility Owner.

Supported billing cycles:

* Daily
* Weekly
* Monthly
* Specific date range
* Custom date range

The MVP can begin with monthly billing while keeping the data model flexible enough to support daily, weekly, and specific-date billing later.

---

## Report Types

The Platform should support reports that help Facility Owners and Platform Administrators understand booking activity and platform fee billing.

MVP report types:

* Daily booking report
* Weekly booking report
* Monthly booking report
* Court utilization report
* Platform fee billing report
* Facility Owner billing report

Future report types:

* Revenue analytics
* Court performance analytics
* Facility performance analytics
* Peak-hour utilization
* Customer repeat booking analytics
* Platform fee aging report

---

## Non-Negotiable Business Rules

* Customer pays the Facility Owner directly.
* Customer pays court rental amount plus platform fee.
* Platform fee is configurable per Facility Owner agreement.
* Facility Owner verifies customer payment receipts.
* Platform never receives customer booking payments directly.
* Platform never holds customer funds.
* Platform never settles payouts to Facility Owners.
* Platform tracks platform fees from confirmed bookings.
* Platform bills Facility Owners, not Customers.
* Every booking belongs to one Facility Owner and one court.
* Every billing report must be traceable to booking records.

---

## Minimum Viable Product (MVP) Business Constraints

The MVP should keep the business model operationally lean.

MVP constraints:

* No direct online payment processing by the Platform
* No automated payout system
* No wallet system
* No escrow system
* No refund handling inside the Platform
* No multi-party split payments
* No automated platform fee collection

These constraints reduce financial risk, shorten development time, and keep the first version focused on the actual booking, receipt upload, verification, and platform fee billing workflow.

---

## Future Monetization Options

The Platform may later support additional pricing models after the MVP proves the booking workflow.

Possible future options:

* Per-hour platform fee
* Range-based platform fee
* Cashback percentage per billing cycle
* Monthly SaaS subscription per Facility Owner
* Per-facility platform fee
* Per-court platform fee
* Tiered plans based on number of courts
* Premium reporting features
* SMS or push notification add-ons
* Payment gateway integrations

Any future payment gateway integration must update this document and related workflow documents before implementation.

---

## Related Documents

* `overview.md`
* `user-roles.md`
* `business-process.md`
* `pricing-strategy.md`
* `platform-fee-strategy.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `database-design.md`
* `api-design.md`
