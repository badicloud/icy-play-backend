# Sports Facility Booking & Management Platform

> A cloud-based SaaS platform that helps Facility Owners manage courts, accept booking requests, verify customer payments, and track platform fees through billing reports.

---

# Project Summary

## Vision

Build a cloud-based SaaS platform for sports court booking and facility operations.

The simplified business model has three primary roles:

* Customer
* Facility Owner
* Platform

The Facility Owner is the owner/operator of the court. Customers pay the Facility Owner directly using the Facility Owner's configured payment QR Code or payment instructions.

The Platform does not receive customer payments during booking. Instead, the Customer pays the court rental amount plus the configured platform fee to the Facility Owner. The Platform later bills the Facility Owner for the platform fee based on the agreed billing cycle.

---

# Business Roles

## Customer

Customers search for available courts, create bookings, pay the Facility Owner directly, upload payment receipts, and receive booking confirmations.

Responsibilities:

* Search available courts
* Create bookings
* Pay the Facility Owner directly
* Pay court rental plus platform fee
* Upload payment receipt
* Receive booking confirmation

---

## Facility Owner

The Facility Owner owns or operates the sports courts.

Responsibilities:

* Manage facilities
* Manage courts
* Configure payment QR Code or payment instructions
* Receive customer payments directly
* Verify uploaded payment receipts
* Confirm or reject bookings
* Monitor ongoing and historical bookings
* Review platform fee billing reports
* Pay the Platform fee based on the billing cycle

---

## Platform

The Platform is the SaaS provider.

Responsibilities:

* Record bookings
* Store receipt metadata and uploaded receipt files
* Track platform fees per booking
* Generate booking reports
* Generate platform fee billing reports
* Send billing reports to Facility Owners
* Bill Facility Owners based on agreed billing cycles

The Platform never receives customer booking payments directly.

---

# Payment Workflow

Example:

```text
Court rental amount: 300
Booking duration:    2 hours
Platform rate:       10 per hour
Platform fee:        20
Customer pays:       320
```

The Customer pays the total amount directly to the Facility Owner. The Platform records the platform fee portion and later bills the Facility Owner for that amount.

```text
Customer
  |
  v
Select Court
  |
  v
Select Schedule
  |
  v
Platform calculates rental amount + platform fee
  |
  v
Display Facility Owner QR Code
  |
  v
Customer pays Facility Owner directly
  |
  v
Upload payment receipt
  |
  v
Booking Status = Pending Verification
  |
  v
Facility Owner verifies payment
  |
  v
Booking Confirmed
  |
  v
Platform records booking and platform fee
  |
  v
Platform fee billing report is updated
```

---

# Revenue Model

```text
Customer
  |
  v
Pays Facility Owner directly
  |
  v
Facility Owner keeps court rental amount
  |
  v
Platform bills Facility Owner for platform fees
```

The Platform fee is configurable depending on the agreement with the Facility Owner.

---

# Billing Flow

```text
Platform records confirmed bookings
  |
  v
Platform tracks platform fee per booking
  |
  v
Platform generates billing report
  |
  v
Billing cycle is applied
  |
  v
Facility Owner reviews billing report
  |
  v
Facility Owner pays Platform
```

Supported billing cycles:

* Daily
* Weekly
* Monthly
* Specific date range

---

# Core Business Rules

* Facility Owner is the owner/operator of the court.
* Facility Owner configures their own payment QR Code or payment instructions.
* Customer pays the Facility Owner directly.
* Customer pays court rental amount plus platform fee.
* Platform fee is configurable per agreement.
* Facility Owner verifies uploaded payment receipts.
* Platform never receives customer booking payments directly.
* Platform tracks platform fees from confirmed bookings.
* Platform bills Facility Owners based on the configured billing cycle.
* Every booking belongs to exactly one Facility Owner and one court.
* The system must support multiple Facility Owners, facilities, and courts.

---

# Technology Stack

## Frontend

* Next.js 15
* TypeScript
* Tailwind CSS

## Backend

* ASP.NET Core 10 (.NET 10 / `net10.0`)
* Entity Framework Core
* Dapper
* SignalR
* Hangfire
* FluentValidation
* JWT Authentication
* Unit Testing

## Database

* SQL Server Express

## Infrastructure

* Redis
* Cloudinary
* Mailjet

## Future

* Progressive Web App (PWA)
* Firebase Cloud Messaging
* PayMongo
* Xendit

---

# Documentation Structure

```text
README.md
FOUNDATION.md
AGENTS.md
PROJECT_CONTEXT.md
docs/
  overview.md
  business-model.md
  user-roles.md
  business-process.md
  pricing-strategy.md
  platform-fee-strategy.md
  booking-workflow.md
  payment-workflow.md
  billing-workflow.md
  notification-workflow.md
  architecture.md
  database-design.md
  api-design.md
  deployment.md
  testing-standards.md
  roadmap.md
```

---

# Development Principles

* Business first, code second.
* Documentation is the single source of truth.
* Never redesign the business model without updating the documentation.
* Maintain clear separation of responsibilities between Platform, Facility Owner, and Customer.
* Build a Minimum Viable Product (MVP) first, then iterate based on real customer feedback.
* Design every feature with scalability and multi-facility operations in mind.
* Cover important business rules with automated tests.

---

# Current Development Phase

**Phase 1 - Identity and Role Foundation**

Current activities:

* Customer and Facility Owner registration
* JWT access tokens and rotating refresh tokens
* Login retry and account lockout
* Role-based authorization foundation
* Identity database schema and migrations

Upcoming milestones:

* Minimum Viable Product (MVP) development
* Internal testing
* Beta release
* Production deployment

---

# License

Private Repository

Copyright 2026.

All Rights Reserved.
