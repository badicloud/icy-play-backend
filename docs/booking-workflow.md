# Booking Workflow

## Purpose

This document defines the end-to-end booking workflow for the Sports Facility Booking & Management Platform.

The workflow follows the simplified business model:

```text
Customer books court.
Customer pays Facility Owner directly.
Facility Owner verifies payment.
Platform records booking and platform fee.
Platform bills Facility Owner later.
```

---

## Primary Actors

### Customer

The Customer searches for available courts, selects a schedule, pays the Facility Owner directly, uploads a payment receipt, and waits for confirmation.

### Facility Owner

The Facility Owner owns or operates the court. The Facility Owner receives the customer payment, verifies the uploaded receipt, and confirms or rejects the booking.

### Platform

The Platform records the booking, calculates the payable amount, stores receipt metadata, tracks platform fees, sends notifications, and updates reports.

---

## Booking Statuses

Recommended initial booking statuses:

* Draft
* Pending Payment
* Pending Verification
* Confirmed
* Rejected
* Cancelled
* Expired

### Draft

The Customer has started a booking flow but has not submitted the booking yet.

### Pending Payment

The booking request is created and the Customer needs to pay the Facility Owner directly.

### Pending Verification

The Customer has uploaded a payment receipt and the booking is waiting for Facility Owner verification.

### Confirmed

The Facility Owner has verified the payment and confirmed the booking.

### Rejected

The Facility Owner rejected the booking because payment was invalid, incomplete, duplicated, missing, or otherwise unacceptable.

### Cancelled

The booking was cancelled by an allowed actor based on cancellation rules.

### Expired

The Customer did not upload a receipt or complete the required action within the allowed time.

---

## High-Level Flow

```text
Customer selects court and schedule
  |
  v
Platform checks availability
  |
  v
Platform calculates rental amount + platform fee
  |
  v
Platform displays Facility Owner payment details
  |
  v
Customer creates booking
  |
  v
Booking status = Pending Payment
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
  +--> Confirmed
  |
  +--> Rejected
```

---

## Detailed Booking Flow

### 1. Customer Searches Courts

The Customer searches for available courts by facility, sport type, date, and time.

The Platform should show only courts that are available for booking based on:

* Court operating hours
* Existing confirmed bookings
* Existing pending bookings within hold window
* Facility availability rules
* Court maintenance blocks
* Cancellation or closure rules

### 2. Customer Selects Court and Schedule

The Customer selects:

* Facility
* Court
* Date
* Start time
* End time
* Duration

The Platform validates that the selected schedule is still available before allowing the booking to continue.

### 3. Platform Calculates Payable Amount

The Platform calculates the full amount the Customer must pay.

The court rental amount is calculated from the Facility Owner's active pricing rules.

Possible pricing rules include:

* Base court rate
* Time-based rate
* Weekend rate
* Holiday rate
* Promotion
* Package rate

Formula:

```text
Customer payable amount = Court rental amount + Platform fee
```

Example:

```text
Court rental amount: 300
Booking duration:    2 hours
Platform rate:       10 per hour
Platform fee:        20
Customer pays:       320
```

The platform fee is configurable depending on the agreement with the Facility Owner.

The calculated court rental amount, applied pricing rule, platform fee, and total payable amount must be stored on the booking so later pricing changes do not affect existing bookings.

### 4. Platform Displays Facility Owner Payment Details

The Platform displays the Facility Owner's configured payment details.

Possible payment details:

* GCash QR Code
* Maya QR Code
* Bank transfer instructions
* Manual payment instructions

The Platform must clearly show:

* Court rental amount
* Platform fee
* Total customer payable amount
* Facility Owner payment QR Code or instructions
* Receipt upload requirement

### 5. Customer Creates Booking

When the Customer confirms the booking request, the Platform creates a booking record.

Initial status:

```text
Pending Payment
```

The booking should store:

* Customer
* Facility Owner
* Facility
* Court
* Date and time
* Court rental amount
* Platform fee
* Total payable amount
* Payment instructions shown to Customer
* Booking status
* Expiration timestamp, if applicable

### 6. Customer Pays Facility Owner Directly

The Customer pays the full payable amount directly to the Facility Owner.

The Platform does not collect this money.

Important rule:

```text
Customer pays Facility Owner directly.
Platform does not receive customer booking payment.
```

### 7. Customer Uploads Payment Receipt

After payment, the Customer uploads a payment receipt.

The Platform stores:

* Receipt file in Cloudinary
* Receipt file metadata in the database
* Upload timestamp
* Uploaded by Customer ID
* Associated booking ID

After successful upload:

```text
Booking status = Pending Verification
```

### 8. Facility Owner Reviews Receipt

The Facility Owner reviews the uploaded payment receipt.

The Facility Owner should check:

* Amount paid
* Payment reference number
* Payment date and time
* Sender details, if available
* Duplicate receipt possibility
* Whether the payment matches the booking

### 9. Facility Owner Confirms Booking

If the receipt is valid, the Facility Owner confirms the booking.

Result:

```text
Booking status = Confirmed
```

When confirmed:

* Court schedule is reserved
* Customer receives confirmation notification
* Booking appears in confirmed booking reports
* Platform fee is included in platform fee billing reports
* Audit log records the confirmation action

### 10. Facility Owner Rejects Booking

If the receipt is invalid, the Facility Owner rejects the booking.

Possible rejection reasons:

* No valid payment found
* Wrong amount paid
* Receipt is unreadable
* Receipt is duplicated
* Payment does not match booking details
* Payment was sent to the wrong account
* Booking schedule is no longer acceptable due to manual conflict

Result:

```text
Booking status = Rejected
```

When rejected:

* Customer receives rejection notification
* Court schedule may become available again
* Booking is excluded from platform fee billing by default
* Audit log records the rejection action and reason

---

## Status Transition Rules

Recommended status transitions:

```text
Draft -> Pending Payment
Pending Payment -> Pending Verification
Pending Payment -> Expired
Pending Verification -> Confirmed
Pending Verification -> Rejected
Pending Verification -> Cancelled
Confirmed -> Cancelled
```

Invalid transitions:

```text
Rejected -> Confirmed
Expired -> Confirmed
Cancelled -> Confirmed
Confirmed -> Pending Verification
```

Any support override for invalid transitions should require Platform Administrator permission and audit logging.

---

## Availability Rules

The Platform should prevent double booking.

Availability should consider:

* Confirmed bookings
* Pending Payment bookings within active hold window
* Pending Verification bookings
* Court operating hours
* Facility operating hours
* Maintenance blocks
* Manual closures

Recommended MVP behavior:

* Hold the selected slot once booking enters `Pending Payment`.
* Release the slot if the booking expires.
* Keep the slot held while booking is `Pending Verification`.
* Reserve the slot permanently when booking becomes `Confirmed`.
* Release the slot when booking is `Rejected`, unless Facility Owner manually chooses otherwise.

---

## Expiration Rules

The Platform should support booking expiration for unpaid or incomplete bookings.

Example:

```text
Booking created at: 10:00 AM
Payment receipt deadline: 10:15 AM
If no receipt is uploaded by 10:15 AM:
Booking status = Expired
Slot becomes available again
```

Expiration duration should be configurable later.

MVP recommendation:

* Start with a simple configurable payment upload window.

---

## Cancellation Rules

Cancellation rules should be configurable later, but MVP can start simple.

Possible cancellation actors:

* Customer
* Facility Owner
* Platform Administrator for support cases

Possible cancellation rules:

* Customer can cancel before Facility Owner confirms
* Facility Owner can cancel due to operational issues
* Platform Administrator can cancel for audited support reasons

Refund handling is outside the MVP because the Platform does not process customer payments.

If refunds are needed, the Facility Owner handles them directly with the Customer.

---

## Platform Fee Behavior

The Platform fee is tracked for confirmed bookings.

Platform fee rules:

* Platform fee is calculated during booking.
* Platform fee is shown to the Customer as part of total payable amount.
* Customer pays the total amount directly to the Facility Owner.
* Platform fee becomes billable when the booking is confirmed.
* Rejected, expired, and cancelled bookings are excluded from platform fee billing by default.
* Billing reports must trace platform fee totals back to booking records.

Example:

```text
Booking #1001
Court rental amount: 300
Booking duration:    2 hours
Platform rate:       10 per hour
Platform fee:        20
Customer paid:       320
Billable platform fee after confirmation: 20
```

---

## Notifications

Booking workflow should trigger notifications.

### Booking Created

Recipients:

* Facility Owner
* Customer

### Receipt Uploaded

Recipients:

* Facility Owner
* Customer

### Booking Confirmed

Recipients:

* Customer

### Booking Rejected

Recipients:

* Customer

### Booking Expired

Recipients:

* Customer
* Facility Owner, optional

### Booking Cancelled

Recipients:

* Customer
* Facility Owner

Notification channels are defined in `notification-workflow.md`.

---

## Audit Logging

The Platform should record important booking actions.

Audit events:

* Booking created
* Receipt uploaded
* Booking confirmed
* Booking rejected
* Booking cancelled
* Booking expired
* Platform fee calculated
* Platform fee included in billing report

Audit records should include:

* Booking ID
* Actor user ID
* Actor role
* Previous status
* New status
* Timestamp
* Reason or note when applicable

---

## Error and Edge Cases

### Customer Uploads Wrong Receipt

Facility Owner rejects the booking with a reason.

### Customer Pays Wrong Amount

Facility Owner can reject the booking or coordinate manually with the Customer.

### Customer Pays but Does Not Upload Receipt

Booking can expire. Customer must coordinate with Facility Owner manually if payment was sent.

### Duplicate Receipt

Facility Owner rejects the duplicated receipt. Platform should later support duplicate detection using receipt metadata or manual flags.

### Facility Owner Does Not Verify on Time

Booking remains pending verification until acted on. Future versions can add reminders and escalation rules.

### Booking Conflict Happens

The Platform should prevent conflicts automatically. If a manual conflict happens, Facility Owner or Platform Administrator must resolve it with audit logging.

---

## MVP Requirements

The MVP booking workflow should include:

* Court search
* Schedule selection
* Availability check
* Rental amount calculation
* Platform fee calculation
* Facility Owner QR Code/payment instruction display
* Booking creation
* Receipt upload
* Facility Owner verification
* Booking confirmation
* Booking rejection
* Booking status history
* Basic booking notifications
* Platform fee tracking for confirmed bookings

---

## Related Documents

* `overview.md`
* `business-model.md`
* `user-roles.md`
* `pricing-strategy.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `notification-workflow.md`
* `architecture.md`
* `database-design.md`
* `api-design.md`
