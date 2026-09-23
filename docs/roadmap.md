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
* Facility Owner accounts are created by a Platform Administrator, not by
  self-registration.
* Platform Admin can access admin-only APIs.
* Protected endpoints reject unauthorized users.
* Facility Owner scoping pattern is established.

Platform Administrators are seeded from configuration
(`PlatformAdmin:SeedEmails`), because the role that grants every other role has
no way of coming into existence otherwise. The seeder only ever promotes an
account that has already registered; it will not create one, so a mistyped
address is logged rather than quietly becoming a live administrator.


---

## Phase 2: Facility and Court Management

Goal:

Give the platform team a console for onboarding owners and running the facility
inventory, and give owners the screens to manage their own courts afterwards.

Scope:

* Admin console: onboarding wizard, facility owner list, facility inventory *(done)*
* Facility owner contracts: commence, renew, reschedule, end *(done)*
* Facility CRUD, including contact details, map coordinates, slug and photos *(done)*
* Facility amenities: a seeded lookup plus free-text safety measures and rules *(done)*
* Facility operating hours, with a per-court override *(done)*
* Court CRUD *(done)*
* Court maintenance blocks, at facility and court level *(done)*
* Court active/inactive status *(done)*
* Facility Owner ownership rules *(done for the admin console)*

Built beyond the original scope, and worth recording:

* A **sports and events lookup**, managed rather than compiled in. A court is
  hired for occasions as well as played on.
* **Dividing a court per sport**: a basketball floor is three pickleball courts,
  and each is booked and priced on its own.
* A **holiday calendar**, because half the Philippine calendar moves.
* **Court pricing** — four rates per sport with a peak window. This was Phase 3
  scope and has been pulled forward; see below.
* A **public catalogue**, cached, backing the landing page.
* A **courts panel** across the whole platform, so a court can be found without
  knowing which venue it is in.

See [courts-and-pricing.md](courts-and-pricing.md) for what was built and why.

Four decisions shape this phase:

| Decision | Why |
| --- | --- |
| Hours live on the facility, courts may override | An owner with eight courts should not type the same schedule eight times, and the outdoor court that closes early stays expressible |
| The platform team encodes owners | Customers pay owners directly; an unvetted account that becomes bookable is a reputation problem |
| Amenities are a seeded lookup plus free text | "Find courts with parking" only works against structured data; free text carries what no checklist can |
| A cover photo per facility | It is what the booking portal and booking list show |

Exit criteria:

* A Platform Admin can onboard a facility owner end to end and commence them.
* An owner with no live contract is invisible to customers.
* Facility Owner can create courts under their facility.
* Facility hours resolve correctly, including a per-court override and a
  facility outside `Asia/Manila`.
* Facility Owner can block unavailable court schedules.
* Facility Owner cannot access another Facility Owner's records.

Still outstanding in this phase:

* The facility owner's **own** console. Everything built so far is the platform
  admin acting on their behalf; the `RoleGuard` and `app/(facility-owner)`
  route group described in the plan are not built.
* Google Maps Places Autocomplete on the address field. `NEXT_PUBLIC_MAP_KEY` is
  still empty, so addresses are typed and coordinates pasted.

---

## Phase 3: Basic Pricing Foundation

> **Largely done ahead of schedule.** Court rates — standard, peak, weekend and
> holiday, per sport, with a peak window and a holiday calendar — were built
> during Phase 2 because dividing a court made pricing per sport unavoidable.
> Promotional and package pricing remain outstanding.
>
> The platform side is also partly there: each contract term carries an hourly
> rate billed to the owner and a commission percentage taken out of that bill.
> Nothing totals booked hours into a period or produces an invoice yet — the
> arithmetic exists, the bookings to count do not.
>
> See [courts-and-pricing.md](courts-and-pricing.md).

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

> **Built.** Availability, pricing, the three booking kinds, double-booking
> protection, the hold and its expiry, and moving a booking — see
> [booking.md](booking.md). The rule this phase was flagged as not enforcing is
> enforced: one floor hosts one sport at a time, and within that sport its parts
> run side by side.
>
> **Beyond the original scope:** a booking can be **moved** to another court,
> other hours or other dates, and **upgraded** onto dearer hours by paying the
> difference. Neither was planned here. Both exist because this platform never
> holds the customer's money, so there is no refund to offer and moving is what
> is offered instead.

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

> **Built.** The GCash flow, the hold and its expiry, Cloudinary receipts, and
> the venue's desk — a queue of what is waiting, confirm, reject, the court
> diary and a booking's history. The desk is **facility owner and attendant**,
> not the owner alone as scoped below.
>
> The upgrade queue runs through the same desk: a customer pays a balance, the
> desk sees the receipt and approves or declines it. See
> [booking.md](booking.md#upgrading-a-booking).

Goal:

Support direct customer payment to Facility Owner with manual receipt verification.

**The MVP flow, as decided:** the customer pays by **GCash**, to the facility
owner's number or QR code. The booking holds the court for a configured number
of minutes and expires if payment does not arrive. The customer uploads the
GCash receipt, and a **facility attendant or platform admin verifies it**, which
confirms the booking. Nothing automated reads the receipt.

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

> **Partly built: email only.** Twelve transactional letters go today — see
> [notification-workflow.md](notification-workflow.md#what-actually-sends-today)
> for the list and what each one is for. They are sent **after the save and best
> effort**: a decision already made must not be reported as failed because a
> mail provider is down.
>
> Not built: in-app records, SignalR, Hangfire retry, and push of any kind.
> Sending is direct rather than queued.

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
