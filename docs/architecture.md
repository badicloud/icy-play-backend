# Architecture

## Purpose

This document defines the high-level technical architecture for the Sports Facility Booking & Management Platform.

The architecture must support a cloud-based SaaS product with multiple Facility Owners, multiple facilities, multiple courts, and customer-facing booking flows.

---

## Architecture Goals

The system should be designed around these goals:

* Support multi-facility SaaS operations
* Keep role responsibilities separated
* Protect facility, court, booking, and customer data boundaries
* Support online court booking and payment receipt upload
* Support Facility Owner-led payment verification
* Support platform fee tracking
* Support booking reports and platform fee billing reports
* Avoid handling customer booking payments directly inside the Platform
* Keep the Minimum Viable Product (MVP) simple enough to build and validate quickly
* Allow future integrations such as push notifications and payment gateways

---

## High-Level System Diagram

```text
Customer / Facility Owner / Platform Admin
  |
  v
Next.js Frontend
  |
  v
ASP.NET Core API
  |
  +--> SignalR Realtime Notifications
  |
  +--> Hangfire Background Jobs
  |
  +--> Entity Framework Core for CRUD and booking workflows
  |      |
  |      v
  |   SQL Server Express
  |
  +--> Dapper for reports, analytics, and dashboards
         |
         v
      SQL Server Express
  |
  +--> Redis Cache
  |
  +--> Cloudinary Media Storage
  |
  +--> Mailjet Email Service
```

---

## Technology Stack

### Frontend

* Next.js 15
* TypeScript
* Tailwind CSS
* Progressive Web App support, future

The frontend provides separate experiences for Customers, Facility Owners, and Platform Administrators.

### Backend

* ASP.NET Core 9
* Entity Framework Core
* Dapper
* SignalR
* Hangfire
* FluentValidation
* JWT Authentication
* Unit testing

The backend owns business rules, authorization, booking workflows, payment receipt handling, platform fee calculations, reporting, and billing.

### Database

* SQL Server Express

The database stores users, Facility Owners, facilities, courts, schedules, bookings, payment receipt metadata, platform fee configuration, billing records, and audit records.

### Infrastructure

* Redis
* Cloudinary
* Mailjet

Redis supports caching and future distributed coordination needs. Cloudinary stores uploaded media such as payment receipts and facility assets. Mailjet sends transactional emails.

### Future Integrations

* Firebase Cloud Messaging
* PayMongo
* Xendit

Firebase Cloud Messaging should be used first for browser push notifications through the Progressive Web App path. Payment gateway integrations are future features and must not change the MVP rule that Customers pay Facility Owners directly unless the business model documents are updated first.

---

## Application Layers

### Presentation Layer

The presentation layer is the Next.js application.

Responsibilities:

* Render public facility and court search pages
* Render booking flow screens
* Render Customer booking history
* Render Facility Owner booking verification screens
* Render Facility Owner management screens
* Render Platform Admin screens
* Call backend APIs
* Receive realtime updates where needed

The frontend should not enforce business-critical rules by itself. It may improve user experience with validation, but final rules must be enforced by the backend.

### API Layer

The API layer is the ASP.NET Core backend.

Responsibilities:

* Authenticate users
* Authorize role-based and scoped access
* Validate requests
* Execute booking workflows
* Route bookings to the correct Facility Owner and court
* Calculate court rental amount plus platform fee
* Store payment receipt metadata
* Generate reports
* Generate platform fee billing summaries
* Send notifications
* Expose APIs for frontend clients

### Domain Layer

The domain layer contains the core business concepts and rules.

Core concepts:

* Facility Owner
* Facility
* Court
* Schedule
* Booking
* Payment Receipt
* Booking Status
* Platform Fee
* Billing Period
* Platform Invoice
* Report

Business rules in this layer must match the documentation. For example, a Facility Owner verifies payment receipts for bookings on their owned courts, while the Platform tracks and bills platform fees later.

### Data Access Layer

The data access layer uses a hybrid approach:

* Entity Framework Core for CRUD, booking workflows, domain relationships, transactions, and migrations
* Dapper for complex reports, analytics, dashboards, and billing summaries

Responsibilities:

* Map domain entities to database tables
* Apply query filters for Facility Owner and facility scoping where appropriate
* Persist booking state transitions
* Persist platform fee records
* Persist audit records
* Support simple operational queries through Entity Framework Core
* Support complex reporting queries through Dapper

Entity Framework Core should be the default data access choice. Dapper should be used when raw SQL is clearer, faster, or more practical for reporting scenarios.

---

## ORM Decision

The primary Object-Relational Mapper (ORM) for the project is Entity Framework Core.

Entity Framework Core is the best default for this project because it fits the backend stack, supports SQL Server well, provides migrations, handles relationships cleanly, supports change tracking, and works well for booking workflows.

Use Entity Framework Core for:

* Customer management
* Facility Owner management
* Facility management
* Court management
* Booking creation
* Booking confirmation
* Booking rejection
* Payment receipt metadata
* Booking status changes
* Platform fee records
* Transactions
* Migrations
* Standard operational queries

Use Dapper for:

* Daily reports
* Weekly reports
* Monthly reports
* Platform fee billing reports
* Dashboard summaries
* Court utilization reports
* Occupancy reports
* Revenue-style analytics
* Complex SQL queries using aggregates, grouping, window functions, or Common Table Expressions

Recommended usage examples:

```text
Booking confirmation
  |
  v
Application Service
  |
  v
Entity Framework Core
  |
  v
SQL Server
```

```text
Platform fee billing report
  |
  v
Report Service
  |
  v
Dapper
  |
  v
SQL Server
```

---

## Backend Project Structure

For the MVP, the backend should use a simple layered structure.

Recommended structure:

```text
API
  |
  v
Application
  |
  v
Infrastructure
  |
  v
Domain
  |
  v
SQL Server
```

Layer responsibilities:

* API handles HTTP endpoints, authentication entry points, request binding, and responses.
* Application contains use cases, services, validation orchestration, and business workflows.
* Infrastructure contains Entity Framework Core, Dapper, storage, email, background jobs, and external integrations.
* Domain contains core entities, enums, value objects, and business rules.

For MVP, avoid over-engineering the backend structure.

Do not use:

* Generic Repository pattern
* Generic Unit of Work wrapper
* Heavy Clean Architecture ceremony

Reason:

* Entity Framework Core `DbContext` already acts as a repository and unit of work.
* Directly injecting `DbContext` into application services is acceptable for this MVP.
* Dapper can be injected through dedicated reporting/query services.
* Fewer abstractions will keep development faster and easier to maintain as a solo primary developer.

Recommended service flow:

```text
Controller
  |
  v
Application Service
  |
  v
DbContext
  |
  v
SQL Server
```

Recommended reporting flow:

```text
Controller
  |
  v
Report Service
  |
  v
Dapper
  |
  v
SQL Server
```

---

## Testing Architecture

Testing is required for long-term maintainability and SaaS quality.

The MVP should include unit tests for business rules and application workflows, then add integration tests for database behavior and reporting as the system grows.

Recommended backend testing stack:

* xUnit for test framework
* FluentAssertions for readable assertions
* Moq or NSubstitute for mocks when needed
* Microsoft.AspNetCore.Mvc.Testing for API integration tests
* Testcontainers or a dedicated test SQL Server database for integration tests

Recommended frontend testing stack:

* Vitest or Jest for unit tests
* React Testing Library for component behavior tests
* Playwright for end-to-end booking flow tests when the frontend is ready

### Unit Tests

Unit tests should focus on business logic that can run without a real database or external service.

Unit test targets:

* Booking status transitions
* Booking validation rules
* Facility Owner payment verification rules
* Facility Owner permission boundaries
* Customer booking ownership rules
* Platform fee calculation rules
* Billing cycle calculation rules
* Report date range calculations
* FluentValidation validators
* Domain entities and value objects

Examples:

* A Facility Owner can confirm only bookings assigned to their owned courts.
* A Customer cannot view another Customer's booking.
* A booking cannot move from Rejected back to Confirmed without an audited support flow.
* Only confirmed bookings are included in platform fee billing by default.
* A configurable platform fee is included in the customer payable amount.

### Integration Tests

Integration tests should verify behavior that depends on the database, Entity Framework Core, Dapper, or API pipeline.

Integration test targets:

* Entity Framework Core mappings
* EF Core migrations
* Facility Owner query scoping
* Dapper report SQL
* Booking persistence
* Receipt metadata persistence
* Platform fee persistence
* API authentication and authorization
* API request and response behavior

Complex Dapper reports should have integration tests because report SQL can break even when unit tests pass.

### End-to-End Tests

End-to-end tests should be added once the core frontend and backend flows exist.

Important end-to-end flows:

* Customer creates booking and uploads receipt
* Facility Owner confirms booking
* Facility Owner rejects booking
* Facility Owner views bookings and platform fee billing
* Platform Administrator views platform-level reports

### Testing Principles

* Test business rules first.
* Keep unit tests fast and deterministic.
* Use integration tests for database and report queries.
* Avoid mocking Entity Framework Core queries heavily.
* Prefer testing services and domain behavior over controller internals.
* Add regression tests for every important bug fix.
* Keep tests aligned with the documentation.

---

## Background Job Layer

The background job layer uses Hangfire.

Responsibilities:

* Generate scheduled reports
* Generate platform fee billing summaries
* Send report emails
* Process notification retries
* Clean up expired temporary files
* Run future recurring maintenance jobs

---

## Realtime Layer

The realtime layer uses SignalR.

Responsibilities:

* Notify Facility Owners about new pending bookings
* Notify Customers about booking status changes
* Notify Platform Administrators about relevant billing or system activity
* Support live dashboard updates where useful

SignalR should improve responsiveness, but core booking state must still be persisted in the database.

SignalR is for in-app realtime updates while the web app is open. It should be paired with email in Version 1 because it does not reliably notify users when the browser is closed.

Future notification path:

```text
Next.js Web App
  |
  v
Progressive Web App
  |
  v
Firebase Cloud Messaging
  |
  v
Browser Push Notification
```

Browser push notifications should be added before building native Android or iOS apps unless there is a strong business requirement for native mobile features.

---

## File Storage Layer

Cloudinary stores uploaded files and media assets.

Stored files may include:

* Payment receipt images
* Facility Owner QR Code images
* Facility images
* Court images
* Generated report exports

The database should store file metadata and Cloudinary public IDs or secure asset references, not large file blobs.

---

## Email Layer

Mailjet handles transactional email.

Possible email types:

* Booking created
* Receipt uploaded
* Booking confirmed
* Booking rejected
* Report generated
* Platform fee billing report generated
* Platform invoice generated
* Account and security emails

---

## Multi-Facility Data Model

The system must support multiple Facility Owners, facilities, and courts.

Expected ownership hierarchy:

```text
Platform
  |
  v
Facility Owner
  |
  v
Facility
  |
  v
Court
  |
  v
Booking
```

Important rules:

* A Facility Owner can manage multiple facilities.
* A facility can have multiple courts.
* A court belongs to one Facility Owner.
* A booking belongs to exactly one court and one Facility Owner.
* A Customer can create bookings across available facilities.

Authorization must enforce data boundaries at both API and query levels.

---

## Booking State Architecture

Booking state should be explicit and auditable.

Recommended initial statuses:

* Draft
* Pending Payment
* Pending Verification
* Confirmed
* Rejected
* Cancelled
* Expired

Typical flow:

```text
Draft
  |
  v
Pending Payment
  |
  v
Pending Verification
  |
  v
Confirmed
```

Alternative endings:

```text
Pending Verification -> Rejected
Pending Payment -> Expired
Confirmed -> Cancelled
```

Every state change should record:

* Previous status
* New status
* Actor user ID
* Actor role
* Timestamp
* Reason or note when applicable

---

## Payment Architecture

For the MVP, the Platform does not process customer booking payments directly.

Payment architecture rules:

* Facility Owner configures payment details.
* Platform calculates court rental amount plus platform fee.
* Platform displays Facility Owner payment details during booking.
* Customer pays Facility Owner directly.
* Customer uploads payment receipt.
* Platform stores receipt file and metadata.
* Facility Owner verifies receipt.
* Platform records verification result.
* Platform records the platform fee for later billing.

The Platform must not:

* Collect customer booking payments directly
* Hold customer funds
* Split payments between parties
* Settle payouts to Facility Owners
* Refund customer payments

---

## Reporting Architecture

Reports should be generated from persisted booking, platform fee, and billing data.

Report generation can be:

* On demand through API requests
* Scheduled through Hangfire
* Exported as files for download
* Sent by email to Facility Owners

Initial report areas:

* Booking activity
* Court utilization
* Platform fee billing
* Facility Owner billing status

Reports should include enough identifiers to trace totals back to bookings, courts, facilities, Facility Owners, platform fee configuration, and billing periods.

---

## Security Architecture

Security must be enforced through authentication, authorization, scoped data access, validation, and auditing.

Key security concerns:

* JWT authentication
* Role-based authorization
* Facility Owner and facility scoping
* Customer booking ownership checks
* Secure file upload validation
* Receipt file access control
* Audit logs for sensitive actions
* Protection against cross-Facility Owner data access

Sensitive actions should be auditable, especially:

* Payment verification
* Booking confirmation
* Booking rejection
* Booking cancellation
* Platform fee configuration changes
* Billing generation
* Platform admin support overrides

---

## Deployment Shape

Initial deployment can use a simple cloud-hosted layout:

```text
Frontend App
  |
  v
Backend API
  |
  +--> SQL Server Express
  +--> Redis
  +--> Cloudinary
  +--> Mailjet
```

The architecture should allow separate scaling later:

* Frontend hosting
* API hosting
* Background worker hosting
* Database hosting
* Redis hosting
* Media storage
* Email provider

---

## Architecture Principles

* Keep business rules in the backend.
* Keep documentation aligned with implementation.
* Keep customer booking payments outside the Platform's direct money flow for MVP.
* Keep Facility Owner and facility data boundaries explicit.
* Keep booking state transitions auditable.
* Keep platform fee calculations auditable.
* Keep important business rules covered by unit tests.
* Keep Dapper reporting queries covered by integration tests.
* Prefer simple deployable components first.
* Design extension points for future integrations without building them too early.

---

## Related Documents

* `overview.md`
* `business-model.md`
* `user-roles.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `notification-workflow.md`
* `database-design.md`
* `api-design.md`
* `deployment.md`
* `roadmap.md`
