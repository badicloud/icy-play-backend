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

* ASP.NET Core 9 Web API
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

* Sending Mailjet emails
* Generating scheduled billing records
* Sending billing reminders
* Cleaning expired pending bookings
* Retrying failed notification jobs
* Preparing reports

For MVP, Hangfire may run inside the ASP.NET Core API process.

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
* `roadmap.md`
