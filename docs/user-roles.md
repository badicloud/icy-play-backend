# User Roles

## Purpose

This document defines the main user roles in the Sports Facility Booking & Management Platform, including their responsibilities, permissions, and business boundaries.

The simplified model has three primary roles:

* Customer
* Facility Owner
* Platform Administrator

---

## Role Summary

```text
Customer
  |
  v
Creates booking and pays Facility Owner directly
  |
  v
Facility Owner
  |
  v
Verifies payment and manages courts
  |
  v
Platform Administrator
  |
  v
Manages SaaS operations, reports, and platform fee billing
```

---

## Customer

### Description

The Customer is the end user who searches for available courts and creates bookings.

Customers pay Facility Owners directly using the Facility Owner's configured payment QR Code or payment instructions. The payment amount includes the court rental amount plus the configured platform fee.

### Responsibilities

* Register and log in
* Search facilities
* Search available courts
* Select court schedule
* Create booking request
* Pay the Facility Owner directly
* Pay court rental amount plus platform fee
* Upload payment receipt
* Track booking status
* Receive booking confirmation or rejection
* View booking history

### Allowed Actions

* View public facility and court availability
* Create a booking
* Upload a payment receipt for their own booking
* Cancel their own booking if cancellation rules allow it
* View their own booking history
* Receive notifications about their own bookings

### Not Allowed

* Verify payments
* Confirm bookings directly
* Reject bookings directly
* View other customers' bookings
* Manage facilities or courts
* Access platform fee billing reports
* Manage platform fee configuration

### Key Business Rule

The Customer pays the Facility Owner directly. The Customer does not pay the Platform separately.

---

## Facility Owner

### Description

The Facility Owner owns or operates one or more sports facilities and courts. The Facility Owner manages court availability, receives customer payments directly, verifies uploaded payment receipts, and confirms or rejects bookings.

The Facility Owner is responsible for paying accumulated platform fees to the Platform based on the configured billing cycle.

### How a Facility Owner gets an account

Facility Owners do not sign themselves up. A Platform Administrator encodes the
account: business details, permit and identity documents, then the facility
itself. The platform is sales-led, and customers pay owners **directly**, so an
account that could become bookable without anyone from the Platform having
looked at it turns a scam into IcyPlay's reputation problem.

Encoding an owner does not make them bookable. That takes a **contract**: a
commencement period with a start and an end date. Until a contract covers today
the owner sits at Pending, visible only to the Platform. When the term ends they
return to Expired and stop being bookable until it is renewed. The same period
is what platform fee pricing will later be based on.

### Responsibilities

* Register and manage facilities
* Create and manage courts
* Configure court pricing
* Configure operating hours
* Configure payment QR Code or payment instructions
* Receive customer payments directly
* Review uploaded payment receipts
* Confirm valid bookings
* Reject invalid or unpaid bookings
* Monitor ongoing bookings
* View historical bookings
* Review booking reports
* Review platform fee billing reports
* Pay Platform fees based on billing cycle

### Allowed Actions

* Create and update facilities
* Create and update courts
* Configure court availability
* Configure payment details
* View pending bookings for owned courts
* Verify payment receipts for owned bookings
* Confirm bookings for owned courts
* Reject bookings for owned courts
* View booking history for owned facilities and courts
* View platform fee billing reports
* Export reports

### Not Allowed

* Access facilities owned by other Facility Owners
* View other Facility Owners' billing reports
* Manage global SaaS settings
* Change another Facility Owner's platform fee configuration
* Mark Platform billing as paid without actual payment confirmation
* Act as the Platform Administrator

### Key Business Rule

The Facility Owner receives the full customer payment and later pays the Platform fee based on billing reports.

---

## Platform Administrator

### Description

The Platform Administrator manages the SaaS platform itself. This role is internal to the platform provider.

Platform Administrators can manage global platform operations, support Facility Owner accounts, configure platform fee agreements, generate billing reports, and monitor system-wide activity.

### Responsibilities

* Onboard Facility Owner accounts, including their permits and documents
* Commence, renew and end Facility Owner contracts
* Manage Facility Owner accounts
* Manage facilities at platform level
* Support Customer account issues
* Configure platform settings
* Configure platform fee agreements
* Monitor system-wide booking activity
* Generate platform fee billing reports
* Manage Platform invoices
* Review analytics and operational reports
* Maintain SaaS configuration

### Allowed Actions

* View platform-wide facilities
* View platform-wide users
* View platform-wide booking reports
* Configure platform fee rules by agreement
* Generate platform fee billing reports
* Generate Platform invoices
* Manage SaaS settings
* Disable abusive or invalid accounts
* Support account recovery and operational issues

### Not Allowed

* Receive customer booking payments directly as part of the MVP flow
* Verify customer payment receipts as a normal booking operation
* Move customer funds between parties
* Act as a payment processor
* Bill Customers directly for platform fees

### Key Business Rule

The Platform Administrator manages SaaS operations and platform fee billing, not direct customer booking payments.

---

## Role Boundaries

| Capability | Customer | Facility Owner | Platform Administrator |
| --- | --- | --- | --- |
| Search courts | Yes | Yes | Yes |
| Create booking | Yes | Optional | No |
| Pay Facility Owner directly | Yes | No | No |
| Upload receipt | Yes | Optional | No |
| Verify customer payment | No | Yes | Support only |
| Confirm booking | No | Yes | Support only |
| Reject booking | No | Yes | Support only |
| Manage owned courts | No | Yes | Support only |
| Manage facilities | No | Own only | Yes |
| Configure payment QR Code | No | Yes | Support only |
| Configure platform fee | No | Agreement view only | Yes |
| View booking reports | Own only | Own facilities | Yes |
| View platform fee billing | No | Own account | Yes |
| Bill Facility Owners | No | No | Yes |
| Receive customer payments | No | Yes | No |

---

## Multi-Facility Rules

* A Facility Owner can own or manage multiple facilities.
* A facility can have multiple courts.
* A court belongs to one Facility Owner.
* A booking belongs to exactly one court and one Facility Owner.
* A Customer can create bookings across available facilities.
* A Facility Owner can manage only their own facilities, courts, bookings, and billing reports.
* Platform Administrators can view system-wide data for support, reporting, and billing.

---

## Permission Design Notes

The authorization system should support role-based access with Facility Owner and facility scoping.

Recommended permission dimensions:

* Global role
* Facility Owner scope
* Facility scope
* Court scope
* Booking ownership
* Report visibility
* Platform fee billing visibility

Examples:

* A Facility Owner can verify only bookings assigned to their owned courts.
* A Customer can view only their own bookings.
* A Platform Administrator can view platform-wide data but should use audited actions for sensitive support operations.
* Platform fee configuration should be restricted to Platform Administrators.

---

## Related Documents

* `overview.md`
* `business-model.md`
* `business-process.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `database-design.md`
* `api-design.md`
