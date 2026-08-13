# Billing Workflow

## Purpose

This document defines how the Platform bills Facility Owners for platform fees.

The billing workflow follows the simplified business model:

```text
Customer pays Facility Owner directly.
Platform tracks platform fee from confirmed bookings.
Platform bills Facility Owner based on billing cycle.
Facility Owner pays Platform.
```

The Platform does not receive customer booking payments directly.

---

## Billing Model

The Platform earns a platform fee from confirmed bookings.

The default platform fee model is per booked hour, with optional range-based discounts for longer bookings.

During booking, the Customer pays:

```text
Court rental amount + Platform fee
```

Example:

```text
Court rental amount: 300
Platform fee:        10
Customer pays:       310
```

The Customer pays the full amount directly to the Facility Owner. The Platform later bills the Facility Owner for the accumulated platform fees.

If cashback is configured, the billing report deducts cashback from the gross platform fee total.

Example:

```text
Gross platform fees: 1,000
Cashback:           5%
Cashback amount:    50
Net amount due:     950
```

---

## Primary Actors

### Facility Owner

The Facility Owner receives customer payments directly, reviews platform fee billing reports, and pays the Platform based on the billing cycle.

### Platform Administrator

The Platform Administrator manages billing cycles, platform fee configuration, cashback configuration, billing report generation, invoice status, adjustments, and payment tracking.

### Platform

The Platform tracks confirmed bookings, calculates platform fees, groups fees by billing cycle, generates billing reports, sends notifications, and records billing status.

---

## Billing Cycle Options

Billing cycles should be configurable per Facility Owner.

Supported billing cycles:

* Daily
* Weekly
* Monthly
* Specific date range
* Custom date range

MVP recommendation:

```text
Start with monthly billing, but design the data model to support daily, weekly, and custom ranges.
```

---

## Billing Statuses

Recommended billing statuses:

* Draft
* Generated
* Sent
* Viewed
* Paid
* Partially Paid
* Overdue
* Cancelled
* Adjusted

### Draft

The billing report is being prepared but is not yet final.

### Generated

The billing report has been generated and totals are calculated.

### Sent

The billing report or invoice has been sent to the Facility Owner.

### Viewed

The Facility Owner has opened or viewed the billing report.

### Paid

The Facility Owner has fully paid the Platform.

### Partially Paid

The Facility Owner paid only part of the billed amount.

### Overdue

The billing due date has passed and the billing record remains unpaid or partially paid.

### Cancelled

The billing record was cancelled by an authorized Platform Administrator.

### Adjusted

The billing record has an approved adjustment.

---

## High-Level Billing Flow

```text
Booking becomes Confirmed
  |
  v
Platform records platform fee
  |
  v
Billing cycle closes
  |
  v
Platform groups billable platform fees
  |
  v
Platform applies cashback, if configured
  |
  v
Platform generates billing report
  |
  v
Platform sends billing report to Facility Owner
  |
  v
Facility Owner reviews report
  |
  v
Facility Owner pays Platform
  |
  v
Platform marks billing record as Paid
```

---

## Detailed Billing Flow

### 1. Booking Is Confirmed

When a Facility Owner verifies payment and confirms a booking, the Platform marks the platform fee as billable.

Billable booking conditions:

* Booking status is `Confirmed`
* Platform fee amount is greater than or equal to zero
* Booking belongs to a Facility Owner
* Booking belongs to a billing period
* Booking is not already included in a finalized billing record

### 2. Platform Records Platform Fee

The Platform stores the platform fee amount calculated during booking.

Stored billing-related values:

* Booking ID
* Facility Owner ID
* Facility ID
* Court ID
* Booking date and time
* Court rental amount
* Platform fee amount
* Platform fee model
* Booking duration in minutes
* Rate per hour
* Applied range rule, if any
* Total customer payable amount
* Platform fee configuration used
* Billing eligibility status

The platform fee stored with the booking should not be recalculated when future platform fee configuration changes.

Cashback should not change the stored booking platform fee. Cashback is calculated when the billing record is generated.

### 3. Billing Cycle Closes

The Platform determines which bookings belong to a billing period.

Examples:

Daily:

```text
2026-06-30 00:00:00 to 2026-06-30 23:59:59
```

Weekly:

```text
Monday 00:00:00 to Sunday 23:59:59
```

Monthly:

```text
2026-06-01 00:00:00 to 2026-06-30 23:59:59
```

Specific date range:

```text
2026-06-15 00:00:00 to 2026-06-30 23:59:59
```

The billing timezone should be consistent and documented. MVP can use the business timezone configured for the Platform or Facility Owner.

### 4. Platform Generates Billing Report

The Platform generates a billing report for the Facility Owner.

The report should include:

* Facility Owner
* Billing period
* Billing cycle
* Booking count
* Total court rental amount
* Total customer-paid amount
* Gross platform fee amount
* Cashback percentage, if configured
* Cashback amount
* Adjustments
* Net amount due
* List of included bookings
* Generated timestamp
* Due date
* Billing status

The report must be traceable to individual booking records.

### 5. Platform Sends Billing Report

The Platform sends the billing report to the Facility Owner.

Channels:

* Dashboard notification
* Mailjet email
* Browser push in Version 1.5

After sending:

```text
Billing status = Sent
```

### 6. Facility Owner Reviews Billing Report

The Facility Owner reviews the billing report and checks the included bookings.

Facility Owner can review:

* Included confirmed bookings
* Platform fee per booking
* Billing period
* Gross platform fee due
* Cashback amount
* Net amount due
* Due date

MVP can keep disputes manual. Future versions may include dispute workflows.

### 7. Facility Owner Pays Platform

The Facility Owner pays the Platform based on the billing report.

For MVP, payment may be tracked manually by the Platform Administrator.

Possible future payment methods:

* Bank transfer
* GCash or Maya transfer
* PayMongo
* Xendit
* Other payment gateway

### 8. Platform Marks Billing as Paid

Once payment is confirmed, the Platform Administrator marks the billing record as paid.

Result:

```text
Billing status = Paid
```

The Platform should store:

* Paid amount
* Paid date
* Payment reference
* Payment method
* Marked paid by user ID
* Notes, if applicable

---

## Billable and Non-Billable Bookings

### Billable by Default

Bookings are billable by default when:

* Booking status is `Confirmed`
* Platform fee was calculated
* Booking is inside the billing period
* Booking has not already been billed

### Not Billable by Default

Bookings are not billable by default when:

* Booking status is `Draft`
* Booking status is `Pending Payment`
* Booking status is `Pending Verification`
* Booking status is `Rejected`
* Booking status is `Expired`

### Needs Policy

Confirmed bookings that are later cancelled need a documented policy.

Possible options:

* Platform fee remains billable after confirmation
* Platform fee is reversed if cancelled before play time
* Platform fee is reversed only if Platform Administrator approves adjustment

MVP recommendation:

```text
Do not finalize confirmed-cancelled billing behavior until business policy is approved.
```

---

## Adjustments

Billing adjustments may be needed for support or business reasons.

Possible adjustment reasons:

* Incorrect platform fee configuration
* Duplicate booking
* Confirmed booking later cancelled
* Manual goodwill adjustment
* Data correction
* Facility Owner dispute

Adjustment rules:

* Only Platform Administrators can create adjustments.
* Adjustments must require a reason.
* Adjustments must be auditable.
* Adjustments must not edit original booking amounts directly.
* Adjustments should appear separately in billing reports.

---

## Due and Overdue Rules

Each billing record should have a due date.

Due date can be configured based on:

* Billing cycle
* Facility Owner agreement
* Platform default setting

Example:

```text
Monthly billing period: June 1 to June 30
Billing generated: July 1
Due date: July 7
```

If unpaid after due date:

```text
Billing status = Overdue
```

Overdue billing should trigger notifications to:

* Facility Owner
* Platform Administrator

---

## Notifications

Billing workflow should trigger notifications.

### Billing Report Generated

Recipients:

* Facility Owner
* Platform Administrator, optional

### Billing Report Sent

Recipients:

* Facility Owner

### Billing Due Reminder

Recipients:

* Facility Owner

### Billing Overdue

Recipients:

* Facility Owner
* Platform Administrator

### Billing Paid

Recipients:

* Facility Owner
* Platform Administrator

Notification channels are defined in `notification-workflow.md`.

---

## Reporting Requirements

Billing reports should support:

* Facility Owner filter
* Facility filter
* Court filter
* Billing period filter
* Booking status filter
* Paid or unpaid status filter
* Export to CSV, Excel, or PDF in future

Recommended MVP report columns:

* Booking ID
* Booking date
* Facility
* Court
* Customer name
* Court rental amount
* Platform fee
* Total customer-paid amount
* Booking status
* Confirmation date

Summary totals:

* Total bookings
* Total court rental amount
* Total customer-paid amount
* Gross platform fee due
* Cashback percentage
* Cashback amount
* Adjustments
* Net amount due

---

## Audit Logging

The Platform should record billing-related actions.

Audit events:

* Platform fee marked billable
* Billing report generated
* Billing report sent
* Billing report viewed
* Billing marked paid
* Billing marked partially paid
* Billing marked overdue
* Billing cancelled
* Billing adjustment created

Audit records should include:

* Billing record ID
* Facility Owner ID
* Actor user ID
* Actor role
* Previous status
* New status
* Timestamp
* Amount affected
* Reason or note when applicable

---

## Background Jobs

Hangfire should be used for scheduled billing work.

Recommended jobs:

* Generate daily billing reports
* Generate weekly billing reports
* Generate monthly billing reports
* Send billing report emails through Mailjet
* Send billing due reminders
* Mark unpaid billing records as overdue
* Send overdue notifications

Jobs should be idempotent so reruns do not duplicate billing records.

---

## Dapper Reporting

Billing reports are a good use case for Dapper.

Use Dapper for:

* Aggregating platform fees
* Grouping bookings by Facility Owner
* Grouping bookings by billing period
* Summing totals
* Export-oriented report queries
* Aging reports

Use Entity Framework Core for:

* Creating billing records
* Updating billing statuses
* Creating adjustments
* Recording audit logs
* Managing billing configuration

---

## MVP Requirements

The MVP billing workflow should include:

* Platform fee tracking from confirmed bookings
* Configurable billing cycle per Facility Owner
* Monthly billing support at minimum
* Billing report generation
* Billing status tracking
* Facility Owner billing dashboard
* Platform Administrator billing dashboard
* Mailjet billing notifications
* Manual payment marking by Platform Administrator
* Audit logging for billing actions

---

## Related Documents

* `overview.md`
* `business-model.md`
* `user-roles.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `notification-workflow.md`
* `architecture.md`
* `database-design.md`
* `api-design.md`
