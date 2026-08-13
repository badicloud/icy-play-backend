# Overview

## Purpose

The Sports Facility Booking & Management Platform is a cloud-based SaaS system for digitizing sports court booking operations. It connects Customers and Facility Owners through a booking, payment receipt verification, reporting, and platform fee billing workflow.

The platform exists to reduce manual booking coordination, centralize court operations, and give Facility Owners a clear system of record for bookings and platform fee billing.

---

## Product Vision

Build a scalable SaaS platform that supports multiple Facility Owners, multiple facilities, and multiple courts while keeping payment responsibility clear and simple.

The platform focuses on:

* Court booking
* Facility management
* Court management
* Booking verification
* Platform fee tracking
* Billing and reporting
* SaaS platform management

---

## Target Users

### Customer

The Customer searches for available courts, creates a booking, pays the Facility Owner directly, uploads a payment receipt, and waits for booking confirmation.

The Customer pays the court rental amount plus the configured platform fee.

### Facility Owner

The Facility Owner owns or operates sports courts. The Facility Owner manages court availability, configures payment QR Code or payment instructions, receives customer payments directly, verifies uploaded payment receipts, and confirms or rejects bookings.

The Facility Owner pays the Platform fee based on the agreed billing cycle.

### Platform

The Platform is the SaaS provider. It records bookings, tracks platform fees, stores receipt metadata, generates booking reports, prepares platform fee billing reports, and sends billing reports to Facility Owners.

The Platform never receives customer booking payments directly.

---

## Business Model Summary

Customer payments are handled outside the Platform's money flow. The Customer pays the Facility Owner directly using the Facility Owner's configured payment QR Code or payment instructions.

The payable amount is:

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

After payment, the Customer uploads a payment receipt. The booking becomes pending verification until the Facility Owner confirms or rejects the payment.

The Platform records confirmed booking activity and tracks the platform fee amount. The Platform later bills the Facility Owner based on the configured billing cycle.

```text
Customer
  |
  v
Pays Facility Owner directly
  |
  v
Facility Owner verifies payment
  |
  v
Platform records confirmed booking and platform fee
  |
  v
Platform bills Facility Owner
```

---

## Core Scope

The initial product scope includes:

* Customer court search and booking
* Facility Owner payment QR Code setup
* Payment receipt upload
* Facility Owner booking verification
* Facility and court management
* Booking monitoring
* Booking reports
* Platform fee configuration
* Platform fee billing reports

---

## Out of Scope for Minimum Viable Product (MVP)

The Minimum Viable Product (MVP) should avoid features that add financial or operational complexity before the core booking workflow is proven.

The following are future considerations:

* Direct payment processing by the Platform
* Automated online collection of platform fees
* Automated payouts
* PayMongo integration
* Xendit integration
* Firebase Cloud Messaging
* Advanced revenue analytics
* Complex subscription plans

---

## Core Business Rules

* Facility Owner is the owner/operator of the court.
* Facility Owner configures payment QR Code or payment instructions.
* Customer pays the Facility Owner directly.
* Customer pays court rental amount plus platform fee.
* Platform fee is configurable per Facility Owner agreement.
* Facility Owner verifies uploaded payment receipts.
* Platform never receives customer booking payments directly.
* Platform tracks platform fees from confirmed bookings.
* Platform bills Facility Owners based on billing cycle.
* Every booking belongs to exactly one Facility Owner and one court.
* The system must support multiple Facility Owners, facilities, and courts.

---

## High-Level Booking Lifecycle

```text
Customer selects court and schedule
  |
  v
Platform calculates rental amount + platform fee
  |
  v
Platform displays Facility Owner payment details
  |
  v
Customer pays Facility Owner directly
  |
  v
Customer uploads receipt
  |
  v
Booking becomes Pending Verification
  |
  v
Facility Owner verifies payment
  |
  v
Booking is Confirmed or Rejected
  |
  v
Booking reports and platform fee billing summaries are updated
```

---

## Platform Principles

* Business first, code second.
* Documentation is the single source of truth.
* Keep role responsibilities clear and separate.
* Build the MVP around the real booking, receipt upload, and verification workflow.
* Design for multiple Facility Owners, facilities, and courts from the start.
* Avoid introducing direct payment-handling responsibilities unless the business model changes.

---

## Related Documents

* `business-model.md`
* `user-roles.md`
* `business-process.md`
* `pricing-strategy.md`
* `platform-fee-strategy.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `notification-workflow.md`
* `architecture.md`
* `database-design.md`
* `api-design.md`
* `deployment.md`
* `roadmap.md`
