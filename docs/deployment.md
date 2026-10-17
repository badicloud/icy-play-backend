# Deployment

## Purpose

This document defines the recommended deployment approach for the Sports Facility Booking & Management Platform.

The deployment plan should support:

* Next.js frontend hosting
* ASP.NET Core backend hosting
* SQL Server database hosting
* Redis
* Cloudinary media storage
* Mailjet transactional email
* Hangfire background jobs
* SignalR realtime notifications
* Secure configuration management
* Future Progressive Web App and mobile app clients

---

## Deployment Goals

The deployment setup should be simple enough for the Minimum Viable Product (MVP), but structured enough to grow into a production SaaS platform.

Goals:

* Keep frontend and backend independently deployable
* Protect secrets and connection strings
* Support repeatable deployments
* Run database migrations safely
* Support background jobs for billing, notifications, and scheduled tasks
* Store uploaded files outside the database
* Support multiple environments
* Make failures visible through logs and monitoring
* Keep the Platform ready for future mobile app consumption

---

## Recommended Environments

The project should use separate environments.

| Environment | Purpose |
| --- | --- |
| Local | Developer machine setup |
| Development | Shared test deployment for early validation |
| Staging | Production-like testing before release |
| Production | Live customer-facing system |

For the MVP, Local and Production may be enough at the beginning, but Staging should be added before onboarding real Facility Owners.

---

## High-Level Deployment Diagram

```text
Users
  |
  v
Next.js Frontend
  |
  v
ASP.NET Core API
  |
  +--> SQL Server
  |
  +--> Redis
  |
  +--> Hangfire Jobs
  |
  +--> SignalR Hub
  |
  +--> Cloudinary
  |
  +--> Mailjet
```

---

## Deployment Units

### Frontend

Application:

* Next.js 15
* TypeScript
* Tailwind CSS

Deployment options:

* Vercel
* Azure Static Web Apps
* Docker container
* Node.js hosting

Recommended MVP option:

* Vercel for simple Next.js deployment

The frontend should communicate with the backend through an environment-configured API base URL.

Example:

```text
NEXT_PUBLIC_API_BASE_URL=https://api.icypay-booking.com/api/v1
```

### Backend

Application:

* ASP.NET Core 10 Web API targeting .NET 10 (`net10.0`)
* SignalR
* Hangfire
* FluentValidation
* JWT Authentication

Deployment options:

* Azure App Service
* Docker container on a VPS
* Render
* Railway
* Fly.io
* AWS Elastic Beanstalk

Recommended MVP option:

* Azure App Service or Docker-based VPS deployment

The backend should own all business rules, authorization, booking status changes, platform fee calculation, and billing generation.

### Database

Database:

* SQL Server Express for local development and early MVP

Production options:

* Azure SQL Database
* Managed SQL Server
* SQL Server running on a VPS

Recommended production direction:

* Use a managed SQL Server service when budget allows.

SQL Server Express is acceptable for local development and early testing, but production should eventually move to a managed database for backups, reliability, and scaling.

#### AUTO_CLOSE is turned off by a migration

**SQL Server Express sets `AUTO_CLOSE` on for every database it creates**,
whatever the model database says — every developer's copy, and the integration
test database on every run. It shuts the database down when the last connection
drops and recovers it on the next one, throwing away the buffer pool and the
plan cache each time.

Against a connection pool it reads as a query that is instant whenever you
measure it and times out for the customer. The court listing was taking **thirty
seconds on a cold start and seven milliseconds once warm, on twenty-six rows** —
so a slow catalogue query is worth checking here before it is blamed on the
query or the indexes.

It lives in a migration (`TurnOffAutoCloseOnSqlServerExpress`) rather than in
this runbook because it arrives switched on in places nobody would think to
check. The migration:

* runs **outside a transaction**, because `ALTER DATABASE … SET` cannot run
  inside one and EF wraps a migration in a transaction by default
* skips engine edition 5 (Azure SQL Database), which has no such setting and
  would fail the deployment over a thing it has already done
* catches its own failure and prints instead of stopping a release — the
  setting needs `ALTER` on the database, which a deployment account is not
  always given, and this is a performance setting rather than a schema change
* has a deliberately empty `Down`: putting `AUTO_CLOSE` back would only
  reintroduce the stall, and nothing in the schema depends on it

### Redis

Redis is used for:

* Caching
* Future distributed coordination
* Future SignalR scale-out if needed

Deployment options:

* Upstash Redis
* Azure Cache for Redis
* Redis container

For MVP, Redis can be added only when a feature needs it. Do not make the first deployment harder than necessary.

### Cloudinary

Cloudinary stores uploaded media such as:

* Facility images
* Court images
* Payment QR codes
* Customer payment receipts
* Open play cover and background photos (`icyplay/open-plays`)
* Open play result photos — best players, winning team, the group — in
  `icyplay/open-play-results`, kept two days and then deleted by the cleanup
  job above. The API key needs permission to delete (the destroy API), not
  only to upload.

The API should store only Cloudinary metadata in SQL Server.

Stored metadata may include:

* Public ID
* Secure URL
* File name
* Content type
* File size
* Upload type
* Uploaded by user ID

### Mailjet

Mailjet sends transactional emails such as:

* Booking receipt uploaded
* Booking confirmed
* Booking rejected
* Billing report generated
* Billing reminder
* Account-related emails

Email sending should be queued through Hangfire so API requests are not delayed by external email service calls.

The open play letters (`open-play-payment-received`, `open-play-payment-submitted`
and `open-play-confirmed`) show a best player voucher on its own line, using the
variables `has_voucher`, `voucher_discount`, `voucher_label`, `has_early_bird` and
`early_bird_discount`. Deploy the API that sends them **before** pasting those
templates into Mailjet: a template asking for a variable the running API does
not send fails to render, and the letter is not sent (it is logged, not thrown).

### PayMongo (online payments)

PayMongo takes online payment for venues whose contract term is set to
**Direct**: court bookings, upgrades and Open Play registrations. Venues set to
**Manual** keep the GCash receipt upload and never touch PayMongo, whatever the
settings below say. The mode is set per contract term by the platform admin.

#### Settings

| Setting | Value | Notes |
| --- | --- | --- |
| `PayMongo__SecretKey` | `sk_live_…` in production, `sk_test_…` elsewhere | Secret. Server side only; the browser never talks to PayMongo |
| `PayMongo__WebhookSecret` | `whsk_…` | Secret. Shown once by PayMongo for **that** webhook; a new webhook has a new one |
| `PayMongo__CheckoutExperience` | `Hosted` (default) or `Integrated` | What step two looks like. Anything else is read as `Hosted` |
| `PayMongo__FeeRates__<method>__Percent` | Optional | Only if PayMongo gives a negotiated rate. The rate it **takes**, VAT included (2.5 for 2.5%) |
| `PayMongo__FeeRates__<method>__Fixed` | Optional | Pesos added per payment, VAT included |

`CheckoutExperience`:

* `Hosted`: step two sends the customer straight to PayMongo's own checkout
  page, where they pick how to pay and PayMongo adds its fee (`pass_on_fees`).
* `Integrated`: step two shows each method with its total before the customer
  picks. QR Ph is shown on our page; GCash and Maya go straight to the
  e-wallet and come back to us whether they paid, failed or gave up; a card
  goes to PayMongo's page offering the card alone.

It is a global setting, not a venue's. Switching it needs only an API restart:
no migration and no frontend change. Attempts already open keep working either
way. If anything goes wrong with `Integrated`, setting it back to `Hosted` is
the rollback.

Default fee rates: what PayMongo actually **takes** from a payment, VAT
included. Its pricing page shows the rates without VAT; what it charges is
that rate with VAT added and rounded to a round figure, times the total, to
the nearest centavo. Every test payment so far matches this.

| Method | Key | Published (VAT excluded) | Taken (VAT included) — what to configure |
| --- | --- | --- | --- |
| QR Ph | `qrph` | 1.34% | **1.5%** |
| GCash | `gcash` | 2.23% | **2.5%** |
| Maya | `paymaya` | 1.79% | **2.0%** |
| Card | — | 3.125% + ₱13.39 | Worked out by PayMongo on its page |

The API rounds the fee **up** where PayMongo rounds to the nearest, so a
customer pays at most a centavo more than PayMongo's own checkout would ask,
and what reaches the venue is never short. Configuring the published rate
instead (2.23 for GCash) leaves payments a centavo short: they arrive flagged
"Needs attention" and the booking is not confirmed.

Example of a negotiated rate:

```text
PayMongo__FeeRates__gcash__Percent=2.0
```

#### Webhook

Create one webhook in the PayMongo dashboard for each mode. Test-mode and
live-mode webhooks are separate, and a live payment is only ever reported to a
live webhook.

```text
Endpoint URL: https://<api-host>/api/v1/webhooks/paymongo
```

Events to subscribe to:

| Event | What it does |
| --- | --- |
| `checkout_session.payment.paid` | Confirms a payment made on PayMongo's page (Hosted, and cards under Integrated) |
| `payment.paid` | Confirms a QR Ph, GCash or Maya payment made from our page |
| `payment.failed` | Closes an attempt the e-wallet declined, so the customer can try again |
| `qrph.expired` | Closes a QR code that ran out |

Rules:

* **Editing** a webhook's URL keeps its secret. **Creating** a new one gives a
  new `whsk_…`, which must go into `PayMongo__WebhookSecret` before the API is
  restarted, or every event is turned away as unsigned.
* Every event is checked against the signature. An unsigned or tampered one is
  answered `401` and ignored.
* A webhook that never arrives does not strand a customer: the waiting page
  asks the API every few seconds, and the API asks PayMongo directly. The
  webhook is still what confirms most payments, and the only thing that
  confirms one when the customer has closed the page.

#### Database

These migrations must be applied (EF Core, in order) before the API starts:

| Migration | Adds |
| --- | --- |
| `20261006131511_AddOnlinePayments` | `OnlinePayments`, `PaymentWebhookEvents`; payment mode and online hold on contract terms; payment channel on bookings |
| `20261008045850_SeedBookingPaidOnlineEmailTemplate` | The "paid online" email template row (Mailjet `8416086`) |
| `20261008053833_AddPaymentChannelToUpgradesAndOpenPlay` | How each upgrade and registration is paid |
| `20261008061137_AddDeskOnlineTransactions` | The desk's online transactions and its unseen badge |
| `20261008220852_AddOnlinePaymentRowVersion` | Stops the webhook and a direct check settling one payment twice |
| `20261012055221_AddPaymentIntents` | Method, quoted total, QR code and e-wallet link for payments started on our page |

None of them removes or rewrites data. The new required columns default to
**`Manual`** (payment mode and payment channel) and **15 minutes** (online
hold), so after deploying, every existing contract term, booking, upgrade and
registration carries on exactly as before. **No venue takes online payment
until the platform admin sets its contract term to Direct** (Facility owners →
the owner → Rates and payment). A term that starts in the future can be
started today from the same page.

#### Mailjet templates

The online payment emails use Mailjet templates by ID. If production uses a
different Mailjet account from development, recreate them there from
`docs/email-templates/` and update the IDs in the `EmailTemplates` table:

* `booking-paid-online.html`: the owner and desk are told a booking was paid
  online and is already confirmed
* `booking-confirmed.html`: now carries the receipt section and the processing
  fee line

Confirmation emails attach the receipt as a PDF, made by the API (QuestPDF,
Community licence). Nothing to configure.

#### Test mode only

* With a `sk_test_…` key, a QR Ph code is **still a real code**: scanning it
  with a real bank or e-wallet app pays for real. Our page shows a
  "Simulate payment (test mode)" link beside it instead, opening PayMongo's
  page that pretends to pay it. The link is only shown when PayMongo marks the
  payment as test mode, or the key is a test key. It never appears with a live
  key.
* Test cards (any future expiry, any CVC): `4343434343434345` succeeds;
  `4120000000000007` asks for 3-D Secure (pick Authorize); `4111111111111111`
  is declined.
* The GCash and Maya test pages have Authorize and Fail buttons in place of a
  real login.

#### Local development

PayMongo cannot reach `localhost`, so a webhook needs a tunnel:

```bash
cloudflared tunnel --url http://localhost:5182
```

Use the `https://….trycloudflare.com` address it prints as the test webhook's
endpoint URL, with `/api/v1/webhooks/paymongo` on the end. The address changes
every time `cloudflared` restarts, so **edit** the webhook's URL each time
(editing keeps the secret). Keep `PayMongo__SecretKey` and
`PayMongo__WebhookSecret` in `appsettings.Development.json`, which is not
committed.

---

## Configuration and Secrets

Secrets must not be committed to the repository.

Use environment variables or a secret manager for:

* Database connection string
* JWT signing key
* JWT issuer
* JWT audience
* Cloudinary cloud name
* Cloudinary API key
* Cloudinary API secret
* Mailjet API key
* Mailjet API secret
* PayMongo secret key
* PayMongo webhook secret
* Redis connection string
* Frontend URL
* Backend public URL

Example backend variables:

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Server=...
Jwt__Issuer=icypay-sport-booking
Jwt__Audience=icypay-sport-booking-clients
Jwt__SigningKey=strong-secret-key
Cloudinary__CloudName=cloud-name
Cloudinary__ApiKey=api-key
Cloudinary__ApiSecret=api-secret
Mailjet__ApiKey=api-key
Mailjet__ApiSecret=api-secret
Cloudinary__CloudName=cloud-name
Cloudinary__ApiKey=cloudinary-api-key
Cloudinary__ApiSecret=cloudinary-api-secret
PayMongo__SecretKey=sk_live_...
PayMongo__WebhookSecret=whsk_...
PayMongo__CheckoutExperience=Hosted
PlatformAdmin__SeedEmails__0=first.admin@example.com
Redis__ConnectionString=redis-connection-string
Frontend__BaseUrl=https://app.icypay-booking.com
Backend__BaseUrl=https://api.icypay-booking.com
```

Example frontend variables:

```text
NEXT_PUBLIC_API_BASE_URL=https://api.icypay-booking.com/api/v1
NEXT_PUBLIC_SIGNALR_NOTIFICATIONS_URL=https://api.icypay-booking.com/hubs/notifications
```

---

## Database Migration Strategy

Entity Framework Core migrations should manage database schema changes.

Recommended migration flow:

```text
Developer creates migration
  |
  v
Migration is reviewed with code changes
  |
  v
Deploy backend build
  |
  v
Run migration against target database
  |
  v
Start or restart backend app
```

Local commands:

```bash
dotnet ef migrations add MigrationName
dotnet ef database update
```

Production rules:

* Always back up the production database before major migrations.
* Avoid destructive migrations unless data migration is planned.
* Do not manually edit production tables unless required for incident recovery.
* Keep migrations small and reviewable.
* Run migrations before enabling code paths that depend on new tables or columns.

---

## Background Jobs

Hangfire should run background work such as:

* Deleting open play result photos two days after they are taken (see below)
* Sending Mailjet emails
* Generating scheduled billing records
* Sending billing reminders
* Cleaning expired pending bookings
* Retrying failed notification jobs
* Preparing reports

For MVP, Hangfire may run inside the ASP.NET Core API process.

#### The Hangfire server has to be switched on

`Hangfire:ServerEnabled` is **false** in both `appsettings.json` and
`appsettings.Development.json`, so out of the box nothing runs the jobs. In
production set `Hangfire__ServerEnabled=true` on the API.

On start the API registers one recurring job whatever that setting says:

| Job id | When | What |
| --- | --- | --- |
| `open-play-result-photo-cleanup` | Every hour | Deletes the open play result photos past their two days — and any the desk replaced or removed — from Cloudinary (the destroy API) and from `OpenPlaySessionPhotos`. A photo Cloudinary will not delete is kept and tried again next hour. |

With the server off the job is registered but never runs, and photos stay past
their two days — which the Open Play Policy says they will not. The dashboard
(`/hangfire`, Development only unless `Hangfire:DashboardEnabled`) shows the
job and its last run.

As the system grows, Hangfire workers can be separated into a dedicated worker service.

Recommended MVP setup:

```text
ASP.NET Core API
  |
  +--> HTTP API
  |
  +--> SignalR Hub
  |
  +--> Hangfire Server
```

Future scalable setup:

```text
ASP.NET Core API
  |
  +--> HTTP API
  |
  +--> SignalR Hub

Worker Service
  |
  +--> Hangfire Server
```

---

## Realtime Notifications

SignalR supports realtime browser updates when the user is online.

Examples:

* New booking created
* Receipt uploaded
* Booking confirmed
* Booking rejected
* Billing report generated

SignalR is not the source of truth.

The database remains the source of truth. Clients should refresh API data after receiving realtime events.

For multiple backend instances, add Redis backplane or another SignalR scale-out approach.

---

## Media Upload Deployment Flow

Recommended upload flow:

```text
Frontend requests signed Cloudinary upload parameters
  |
  v
Backend validates authenticated user
  |
  v
Backend returns signed upload parameters
  |
  v
Frontend uploads file to Cloudinary
  |
  v
Cloudinary returns public ID and secure URL
  |
  v
Frontend submits metadata to backend
  |
  v
Backend stores metadata in SQL Server
```

Important rules:

* Do not store image blobs in SQL Server.
* Store Cloudinary public ID and secure URL.
* Validate upload type before accepting metadata.
* Limit accepted file types and file sizes.
* Keep payment receipts tied to the correct booking and customer.

---

## CI/CD Pipeline

Recommended deployment pipeline:

```text
Push to main branch
  |
  v
Run frontend build
  |
  v
Run backend build
  |
  v
Run unit tests
  |
  v
Publish frontend
  |
  v
Publish backend
  |
  v
Run database migrations
  |
  v
Smoke test production endpoints
```

Minimum checks:

* Frontend TypeScript check
* Frontend build
* Backend build
* Backend unit tests
* Database migration check

Recommended backend commands:

```bash
dotnet restore
dotnet build
dotnet test
```

Recommended frontend commands:

```bash
npm install
npm run build
```

---

## Production Release Checklist

Before releasing to production:

* Environment variables are configured.
* Database connection is working.
* Database backup exists.
* EF Core migrations are applied.
* Cloudinary credentials are valid.
* Mailjet credentials are valid.
* JWT signing key is strong.
* CORS allows only approved frontend domains.
* HTTPS is enabled.
* API health check is reachable.
* Frontend can call backend API.
* Login works.
* Booking creation works.
* Receipt upload works.
* Facility Owner verification works.
* Billing generation works.
* Email sending works.
* `Hangfire__ServerEnabled` is true, and the `open-play-result-photo-cleanup` recurring job shows on the Hangfire dashboard.
* The open play Mailjet templates are the ones in `docs/email-templates` (voucher lines), pasted after the API was deployed.
* PayMongo live keys are set (`sk_live_…`), and the webhook secret belongs to the live webhook.
* The live PayMongo webhook points at the production API and subscribes to the four events in the PayMongo section.
* `PayMongo__CheckoutExperience` is the one intended (`Hosted` or `Integrated`).
* One small real payment at a Direct venue confirms by webhook, emails both sides, and shows on the desk's online transactions.
* A Manual venue's booking still asks for a GCash receipt.
* Logs are visible.

---

## Health Checks

The backend should expose a health check endpoint.

Recommended endpoint:

```http
GET /health
```

Health checks should verify:

* API process is running
* SQL Server connection is available
* Redis connection is available, if enabled
* Cloudinary configuration is present
* Mailjet configuration is present

Do not expose sensitive configuration values in health check responses.

---

## Logging and Monitoring

The platform should log important operational events.

Recommended logs:

* User login attempts
* Booking creation
* Receipt upload
* Booking confirmation
* Booking rejection
* Platform fee calculation
* Billing generation
* Billing payment marking
* Email send success or failure
* Background job failure
* Authorization failure
* Unexpected exceptions

Recommended monitoring:

* API errors
* Slow requests
* Failed background jobs
* Database connection issues
* Email delivery failures
* Cloudinary upload failures

Future tools:

* Application Insights
* Seq
* Sentry
* Grafana

---

## Backup and Recovery

Production data must be backed up regularly.

Backup targets:

* SQL Server database
* Environment configuration records
* Cloudinary asset references stored in SQL Server

Cloudinary stores the actual media files, but SQL Server stores the references needed by the application.

Recommended backup frequency:

| Environment | Frequency |
| --- | --- |
| Development | Optional |
| Staging | Before major tests |
| Production | Daily minimum |

Recovery should be tested before real customer usage.

---

## Security Requirements

Deployment must enforce:

* HTTPS only
* Strong JWT signing key
* Secure refresh token handling
* Environment-based secrets
* Role-based authorization
* Facility Owner data scoping
* CORS restrictions
* Request validation
* File upload validation
* No public access to admin-only APIs

Payment receipt images may contain sensitive customer information, so access rules and Cloudinary usage must be reviewed carefully.

---

## Mobile and PWA Deployment Readiness

The deployed API should be ready for future mobile clients.

Requirements:

* Stable `/api/v1` routes
* JWT access and refresh tokens
* Device registration endpoints
* Stable error codes
* CORS configured for web clients
* Push notification path ready for Firebase Cloud Messaging
* Public API base URL accessible outside the frontend hosting provider

For Progressive Web App support, the frontend deployment should later include:

* Web app manifest
* Service worker
* Installable app configuration
* Browser push notification permission flow

---

## MVP Deployment Recommendation

Recommended MVP deployment:

| Component | Recommended Option |
| --- | --- |
| Frontend | Vercel |
| Backend | Azure App Service or Docker VPS |
| Database | SQL Server Express for testing, managed SQL Server for production |
| Media Storage | Cloudinary |
| Email | Mailjet |
| Background Jobs | Hangfire inside ASP.NET Core API |
| Realtime | SignalR inside ASP.NET Core API |
| Redis | Add only when needed |

This keeps the first deployment simple while preserving a path toward a more scalable SaaS setup.

---

## Future Deployment Improvements

After MVP validation, consider:

* Separate worker service for Hangfire
* Managed SQL Server with automated backups
* Redis cache and SignalR scale-out
* Blue-green deployments
* CDN for frontend assets
* Centralized logging
* Error tracking
* Automated smoke tests
* Infrastructure as Code
* Dedicated staging environment

---

## Related Documents

* `overview.md`
* `business-model.md`
* `architecture.md`
* `api-design.md`
* `database-design.md`
* `notification-workflow.md`
* `billing-workflow.md`
* `payment-workflow.md`
* `roadmap.md`
