# Roadmap

## Purpose

This document defines the recommended build order for the Sports Facility Booking & Management Platform.

The roadmap keeps the project focused on a usable Minimum Viable Product (MVP) first, then expands into pricing, reporting, notifications, mobile readiness, and future e-commerce.

---

## Roadmap Principles

Development should follow these principles:

* Build the booking workflow before advanced features.
* Keep the business model simple and documented.
* Do not let future e-commerce complexity slow down the MVP.
* Keep APIs mobile-ready from the start.
* Keep Facility Owner data boundaries strict.
* Store important calculated values on bookings for auditability.
* Use Entity Framework Core for transactional workflows.
* Use Dapper for reports and analytics.
* Add tests around business rules and booking state transitions.

---

## Phase 0: Project Foundation

Goal:

Set up the technical foundation before building business features.

Scope:

* ASP.NET Core 10 backend solution targeting .NET 10 (`net10.0`)
* Project structure
* Domain, Application, Infrastructure, and API layers
* SQL Server Express connection
* Entity Framework Core setup
* Dapper setup
* FluentValidation setup
* JWT authentication setup
* Role-based authorization setup
* Global error handling
* Standard API response shape
* Stable error codes
* Rate limiting
* Login retry and account lockout policy
* Correlation ID support
* Health check endpoint
* Unit test project

Recommended backend structure:

```text
backend/
  src/
    IcyPay.Booking.Api/
    IcyPay.Booking.Application/
    IcyPay.Booking.Domain/
    IcyPay.Booking.Infrastructure/
  tests/
    IcyPay.Booking.UnitTests/
    IcyPay.Booking.IntegrationTests/
```

Exit criteria:

* Backend runs locally.
* Health check works.
* Swagger works.
* Authentication skeleton exists.
* Database connection works.
* First migration can be created.
* Unit test project runs.

---

## Phase 1: Identity and Role Foundation

Status: In progress. Implemented so far:

* Customer and Facility Owner registration
* JWT access tokens and rotating hashed refresh tokens
* Login, logout, refresh, and current-user endpoint
* Account lockout and per-IP rate limiting on every authentication endpoint
* reCAPTCHA v3 on registration, login, forgot password, and resend verification
* Account email verification, with resend and a 24-hour single-use link
* Password reset, with a 60-minute single-use link that also ends every session
* "Remember me", sizing the refresh token to 30 days or 12 hours
* Active session management: list, revoke one, revoke others, revoke all

Platform Administrator provisioning and broader authorization-policy coverage
remain. Nothing yet requires a verified email before booking; that business rule
is still open.

Goal:

Support the core users of the system.

Scope:

* User accounts
* Customer profile
* Facility Owner profile
* Platform Administrator role
* Login
* Refresh token
* Logout
* Account lockout
* Role-based authorization policies
* Facility Owner scoped access foundation

Roles:

* Customer
* FacilityOwner
* PlatformAdmin

Exit criteria:

* Customer can register and log in.
* Facility Owner can register or be onboarded.
* Platform Admin can access admin-only APIs.
* Protected endpoints reject unauthorized users.
* Facility Owner scoping pattern is established.

---

## Phase 2: Facility and Court Management

Goal:

Allow Facility Owners to manage their courts.

Scope:

* Facility CRUD
* Court CRUD
* Court operating hours
* Court maintenance blocks
* Court active/inactive status
* Facility Owner ownership rules

Exit criteria:

* Facility Owner can create a facility.
* Facility Owner can create courts under their facility.
* Facility Owner can define operating hours.
* Facility Owner can block unavailable court schedules.
* Facility Owner cannot access another Facility Owner's records.

---

## Phase 3: Basic Pricing Foundation

Goal:

Calculate court rental amount before booking.

MVP pricing scope:

* Base court rental rate
* Time-based rate
* Weekend rate
* Holiday rate
* Basic package rate
* Basic promotion
* Price priority rules
* Price locking on booking

Rules:

* Facility Owner manages court rental pricing.
* Platform calculates price using active pricing rules.
* Booking stores the calculated rental amount.
* Existing booking prices do not change when pricing rules change.

Exit criteria:

* API can calculate court rental amount for selected court, date, and time.
* Pricing rule priority is documented in code and tests.
* Booking can store applied pricing rule details.

---

## Phase 4: Platform Fee Configuration

Goal:

Support per-hour platform fee calculation per Facility Owner agreement.

Scope:

* Platform fee agreement setup
* Per-hour platform fee
* Range-based platform fee pricing
* Discounted daily platform fee
* Cashback percentage per billing cycle
* Effective date handling
* Facility Owner agreement history
* Platform fee calculation during booking

MVP recommendation:

* Start with configurable per-hour platform fee.
* Add range-based discounts for longer bookings.
* Apply cashback during billing, not during booking.

Exit criteria:

* Platform Admin can configure platform fee agreement.
* Booking calculation includes per-hour or range-based platform fee.
* Booking stores platform fee amount.
* Billing records show gross platform fee, cashback, and net amount due.
* Historical bookings keep their original platform fee.

---

## Phase 5: Booking Workflow

Goal:

Allow Customers to book courts and pay Facility Owners directly.

Scope:

* Public facility and court search
* Court availability check
* Booking creation
* Double booking protection
* Pending payment status
* Payment due timestamp
* Total payable amount calculation
* Facility Owner payment method display
* Booking status history
* Audit logging

Booking statuses:

* PendingPayment
* PendingVerification
* Confirmed
* Rejected
* Cancelled
* Expired

Exit criteria:

* Customer can select a court and schedule.
* System calculates court rental plus platform fee.
* Customer can create a booking.
* Same court slot cannot be double-booked.
* Booking has a clear status history.

---

## Phase 6: Payment Receipt Upload and Verification

Goal:

Support direct customer payment to Facility Owner with manual receipt verification.

Scope:

* Facility Owner payment method setup
* Cloudinary signed upload parameters
* Receipt metadata storage
* Receipt upload status
* Facility Owner verification dashboard
* Confirm booking
* Reject booking
* Customer notification after decision

Rules:

* Platform does not receive the customer payment.
* Customer uploads proof of payment only.
* Facility Owner verifies the receipt.
* Confirmed booking becomes billable for platform fee.

Exit criteria:

* Facility Owner can configure payment QR Code or instructions.
* Customer can upload receipt metadata.
* Facility Owner can confirm or reject booking.
* Booking status changes are audited.

---

## Phase 7: Notifications

Goal:

Notify users about important booking and billing events.

MVP notification scope:

* In-app notification records
* SignalR realtime notifications
* Mailjet transactional emails
* Hangfire background email jobs

Events:

* Booking created
* Receipt uploaded
* Booking confirmed
* Booking rejected
* Billing record generated
* Billing reminder

Future:

* Progressive Web App push notifications
* Firebase Cloud Messaging
* Mobile device push notifications

Exit criteria:

* Facility Owner receives booking and receipt alerts.
* Customer receives confirmation or rejection updates.
* Billing email can be sent through Mailjet.
* Failed notification jobs can be retried.

---

## Phase 8: Billing and Platform Fee Reports

Goal:

Bill Facility Owners for accumulated platform fees.

Scope:

* Confirmed booking platform fee ledger
* Billing cycle configuration
* Billing record generation
* Billing line items
* Send billing report
* Mark billing record as paid
* Billing adjustments
* Facility Owner billing dashboard

Supported billing cycles:

* Daily
* Weekly
* Monthly
* Specific date range
* Custom date range

Exit criteria:

* Platform Admin can generate billing records.
* Billing records include confirmed billable bookings only.
* Facility Owner can view billing records.
* Platform Admin can mark billing records as paid.
* Billing totals are traceable to bookings.

---

## Phase 9: Reporting and Dashboards

Goal:

Provide operational visibility for Facility Owners and Platform Administrators.

Reports:

* Daily booking report
* Weekly booking report
* Monthly booking report
* Court utilization report
* Platform fee billing report
* Facility Owner billing report
* Pricing rule performance report
* Promotion usage report

Technical approach:

* Use Dapper for report-heavy queries.
* Keep report filters explicit.
* Add integration tests for complex report SQL.

Exit criteria:

* Facility Owner can view own reports.
* Platform Admin can view platform-wide reports.
* Reports respect Facility Owner data boundaries.

---

## Phase 10: Frontend MVP

Goal:

Build the first usable web experience.

Customer screens:

* Facility search
* Court details
* Availability selection
* Booking summary
* Payment instruction screen
* Receipt upload
* My bookings

Facility Owner screens:

* Dashboard
* Facility management
* Court management
* Pricing setup
* Payment method setup
* Pending verification bookings
* Booking history
* Billing records

Platform Admin screens:

* Facility Owner management
* Platform fee agreement setup
* Billing generation
* Billing payment tracking
* Reports

Exit criteria:

* End-to-end booking workflow works from UI.
* Facility Owner can verify bookings.
* Platform Admin can generate billing.

---

## Phase 11: Deployment MVP

Goal:

Deploy the first usable production-ready version.

Scope:

* Frontend deployment
* Backend deployment
* SQL Server database deployment
* Cloudinary configuration
* Mailjet configuration
* Environment variables
* Health checks
* Logs
* Database backups
* Production release checklist

Exit criteria:

* App is accessible through production URLs.
* API health check passes.
* Booking workflow works in production.
* Email sending works in production.
* Database backup process exists.

---

## Future Phase: PWA and Mobile Readiness

Goal:

Improve web app experience and prepare for mobile app clients.

Scope:

* Progressive Web App support
* Installable web app
* Browser push notifications
* Firebase Cloud Messaging
* Device registration
* Mobile app API consumption
* Offline-friendly cached reads

---

## Future Phase: E-Commerce Add-ons

Goal:

Allow Facility Owners to sell or rent items together with court bookings.

Future items:

* Bottled water
* Sports drinks
* Shuttlecock
* Pickleball racket rental
* Ball rental
* Towel rental
* Equipment rental
* Merchandise

Future bundles:

* Court rental plus bottled water
* Court rental plus racket rental
* Court rental plus ball rental
* Team package
* Practice package

Important:

E-commerce should be documented separately before implementation because it introduces inventory, item pricing, bundle pricing, stock tracking, and possibly payment workflow changes.

---

## MVP Build Order Summary

Recommended order:

1. Backend foundation
2. Identity and roles
3. Facility and court management
4. Pricing foundation
5. Platform fee configuration
6. Booking workflow
7. Receipt upload and verification
8. Notifications
9. Billing
10. Reports
11. Frontend MVP
12. Deployment MVP

---

## Out of Scope for MVP

Do not include these in the first MVP unless the business model changes:

* Direct platform payment collection
* PayMongo integration
* Xendit integration
* Automated payouts
* Wallet system
* Escrow system
* Refund handling inside the Platform
* Inventory management
* Full e-commerce checkout
* Native mobile app

---

## Related Documents

* `overview.md`
* `business-model.md`
* `user-roles.md`
* `pricing-strategy.md`
* `architecture.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `notification-workflow.md`
* `billing-workflow.md`
* `database-design.md`
* `api-design.md`
* `deployment.md`
