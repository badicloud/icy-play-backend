# Payment Workflow

## Purpose

This document defines how payment works in the Sports Facility Booking & Management Platform.

The payment workflow is designed to keep the MVP simple:

```text
Customer pays Facility Owner directly.
Facility Owner verifies payment.
Platform records platform fee for later billing.
```

The Platform does not receive customer booking payments directly.

---

## Payment Model

The Customer pays the full booking amount directly to the Facility Owner.

The full booking amount is:

```text
Court rental amount + Platform fee
```

Example:

```text
Court rental amount: 300
Booking duration:    2 hours
Platform rate:       10 per hour
Platform fee:        20
Customer pays:       320
```

The Facility Owner receives the full amount from the Customer. The Platform later bills the Facility Owner for the platform fee portion based on the configured billing cycle.

---

## Primary Actors

### Customer

The Customer pays the Facility Owner directly and uploads proof of payment.

### Facility Owner

The Facility Owner configures payment details, receives the customer payment, reviews the uploaded receipt, and confirms or rejects the booking.

### Platform

The Platform displays payment details, calculates the payable amount, stores receipt metadata, records verification results, tracks platform fees, and generates platform fee billing reports.

---

## Payment Setup

Before Customers can pay, the Facility Owner must configure payment details.

Possible payment details:

* GCash QR Code
* Maya QR Code
* Bank transfer instructions
* Manual payment instructions

The payment setup should include:

* Payment method name
* Account name
* Account number or identifier, if applicable
* QR Code image, if applicable
* Payment instructions
* Active or inactive status

Only active payment details should be shown during booking.

---

## Platform Fee Configuration

The Platform fee is configurable depending on the agreement with the Facility Owner.

Possible fee models:

* Per-hour platform fee
* Range-based platform fee
* Discounted daily platform fee
* Custom agreement per Facility Owner
* Cashback percentage per billing cycle

MVP recommendation:

```text
Use configurable per-hour platform fee with optional range-based discounts.
```

Example:

```text
Facility Owner A platform fee: 10 per hour
Facility Owner B platform fee: 8 per hour
Facility Owner C platform fee:
  10 per hour
  4-hour range fee: 35
  10-hour range fee: 90
  1-day range fee: 200
```

The platform fee should be calculated and stored with the booking so future configuration changes do not modify historical booking fee records.

Cashback, if configured, should be applied during billing, not during booking.

---

## High-Level Payment Flow

```text
Customer selects court and schedule
  |
  v
Platform calculates court rental amount
  |
  v
Platform calculates platform fee
  |
  v
Platform displays total payable amount
  |
  v
Platform displays Facility Owner payment details
  |
  v
Customer pays Facility Owner directly
  |
  v
Customer uploads payment receipt
  |
  v
Booking status = Pending Verification
  |
  v
Facility Owner verifies receipt
  |
  +--> Booking Confirmed
  |
  +--> Booking Rejected
```

---

## Detailed Payment Flow

### 1. Platform Calculates Court Rental Amount

The Platform calculates the rental amount based on:

* Court price
* Selected duration
* Date and time rules
* Facility-specific pricing rules
* Future promo or discount rules, if added

### 2. Platform Calculates Platform Fee

The Platform calculates the platform fee using the configured agreement for the Facility Owner.

The calculation should consider:

* Booking duration
* Per-hour platform rate
* Range-based platform fee rules
* Discounted daily fee, if configured

The calculated fee should be stored with the booking.

Stored values:

* Court rental amount
* Platform fee amount
* Total payable amount
* Fee model used
* Fee configuration snapshot or reference

### 3. Platform Displays Total Payable Amount

The Customer must clearly see the full amount to pay.

Example display:

```text
Court rental: 300
Booking duration: 2 hours
Platform rate: 10 per hour
Platform fee: 20
Total to pay: 320
```

The Customer pays the total amount to the Facility Owner.

### 4. Platform Displays Facility Owner Payment Details

The Platform displays active payment details configured by the Facility Owner.

The Platform should show:

* Payment method
* QR Code or payment instructions
* Total payable amount
* Booking reference number
* Reminder to upload receipt after payment

### 5. Customer Pays Facility Owner Directly

The Customer sends payment outside the Platform using the Facility Owner's payment details.

Important rule:

```text
The Platform does not collect, hold, process, split, settle, or refund customer booking payments.
```

### 6. Customer Uploads Receipt

The Customer uploads proof of payment after paying.

Accepted file types should be defined during implementation.

Recommended MVP accepted files:

* JPG
* PNG
* PDF, optional

The Platform stores:

* Receipt file in Cloudinary
* Receipt metadata in SQL Server
* Booking ID
* Uploaded by Customer ID
* Upload timestamp
* File name
* File size
* Content type
* Cloudinary public ID or secure asset reference

After successful upload:

```text
Booking status = Pending Verification
```

### 7. Facility Owner Verifies Receipt

The Facility Owner reviews the receipt manually.

The Facility Owner should verify:

* Amount paid matches total payable amount
* Payment date and time are acceptable
* Payment reference is valid
* Sender information matches expected Customer where possible
* Receipt is readable
* Receipt has not already been used for another booking

### 8. Facility Owner Confirms Payment

If the receipt is valid, the Facility Owner confirms the booking.

Result:

```text
Booking status = Confirmed
```

Effects:

* Booking becomes confirmed
* Customer receives confirmation
* Court schedule remains reserved
* Platform fee becomes billable
* Booking appears in confirmed booking reports
* Audit log records the confirmation

### 9. Facility Owner Rejects Payment

If the receipt is invalid, the Facility Owner rejects the booking.

Result:

```text
Booking status = Rejected
```

Effects:

* Customer receives rejection notification
* Court slot may become available again
* Platform fee is not billable by default
* Audit log records the rejection reason

---

## Verification Rules

Facility Owner should reject payment when:

* Receipt is missing
* Receipt is unreadable
* Paid amount is lower than required
* Payment was sent to the wrong account
* Payment reference is invalid
* Receipt was already used
* Payment does not match the booking

Facility Owner may confirm payment when:

* Receipt is readable
* Paid amount matches total payable amount
* Payment reference appears valid
* Payment details match the booking

---

## Platform Fee Billing Rules

Platform fee billing is separate from customer payment.

Rules:

* Customer pays platform fee as part of the total amount sent to the Facility Owner.
* Facility Owner receives the full customer payment.
* Platform records the platform fee amount for the booking.
* Platform fee becomes billable only after booking confirmation.
* Cashback is applied on the billing record, not on the customer payment screen.
* Platform bills Facility Owner based on billing cycle.
* Rejected and expired bookings are excluded from platform fee billing by default.
* Cancelled confirmed bookings need a documented billing policy.

Billing cycles:

* Daily
* Weekly
* Monthly
* Specific date range
* Custom date range

---

## Payment Statuses

Payment status can be tracked separately from booking status.

Recommended payment statuses:

* Not Submitted
* Receipt Uploaded
* Verified
* Rejected

Suggested mapping:

| Booking Status | Payment Status |
| --- | --- |
| Pending Payment | Not Submitted |
| Pending Verification | Receipt Uploaded |
| Confirmed | Verified |
| Rejected | Rejected |
| Expired | Not Submitted |

---

## What the Platform Does Not Do

For MVP, the Platform does not:

* Collect customer booking payments
* Hold customer funds
* Process online card or wallet payments
* Automatically split payments
* Settle payouts to Facility Owners
* Issue customer refunds
* Automatically collect platform fees from Facility Owners

This keeps the MVP simpler and reduces financial risk.

---

## Notifications

Payment workflow should trigger notifications.

### Receipt Uploaded

Recipients:

* Facility Owner
* Customer

### Payment Verified

Recipients:

* Customer

### Payment Rejected

Recipients:

* Customer

### Platform Fee Billing Generated

Recipients:

* Facility Owner
* Platform Administrator, optional

Notification channels are defined in `notification-workflow.md`.

---

## Audit Logging

The Platform should record payment-related actions.

Audit events:

* Payment details configured
* Platform fee calculated
* Receipt uploaded
* Receipt viewed by Facility Owner
* Payment verified
* Payment rejected
* Platform fee added to billing report

Audit records should include:

* Booking ID
* Facility Owner ID
* Customer ID
* Actor user ID
* Actor role
* Timestamp
* Previous status
* New status
* Reason or note when applicable

---

## Edge Cases

### Customer Pays Wrong Amount

Facility Owner can reject the booking or coordinate manually with the Customer.

### Customer Pays but Uploads No Receipt

Booking remains pending payment until it expires. Customer must coordinate with Facility Owner if payment was sent.

### Customer Uploads Duplicate Receipt

Facility Owner rejects the booking. Future versions may add duplicate detection.

### Facility Owner Changes QR Code

New bookings should use the latest active payment details. Existing bookings should retain the payment details shown at booking time.

### Platform Fee Changes

New bookings should use the latest platform fee configuration. Existing bookings should retain the platform fee calculated at booking time.

### Confirmed Booking Is Cancelled

Platform fee treatment must follow a documented cancellation and billing policy.

MVP recommendation:

* Exclude rejected and expired bookings.
* Decide confirmed-cancelled billing behavior before launch.

---

## MVP Requirements

The MVP payment workflow should include:

* Facility Owner payment QR Code setup
* Platform fee configuration
* Total payable amount calculation
* Payment instruction display
* Receipt upload
* Receipt metadata storage
* Facility Owner payment verification
* Payment rejection reason
* Platform fee tracking for confirmed bookings
* Basic payment notifications
* Audit logging for verification actions

---

## Related Documents

* `overview.md`
* `business-model.md`
* `user-roles.md`
* `booking-workflow.md`
* `billing-workflow.md`
* `notification-workflow.md`
* `architecture.md`
* `database-design.md`
* `api-design.md`
