# API Design

## Purpose

This document defines the initial API design for the Sports Facility Booking & Management Platform.

The API supports:

* Authentication and role-based authorization
* Facility Owner facility and court management
* Customer court search and booking
* Direct payment receipt upload metadata
* Facility Owner payment verification
* Platform fee tracking
* Platform billing to Facility Owners
* Notifications
* Reports

---

## API Style

Recommended style:

* REST API
* JSON request and response bodies
* JWT authentication
* Role-based authorization
* Facility Owner scoped access
* Consistent response and error format
* Pagination for list endpoints
* Dapper-backed report endpoints where query complexity is high

Base URL example:

```text
/api/v1
```

---

## Mobile App Readiness

The same API should support both the Next.js web app and future mobile apps.

The backend should stay independent from any frontend-specific behavior so Android or iOS apps can consume the same endpoints later without redesigning the API.

Recommended mobile-ready API standards:

* Use versioned routes such as `/api/v1`.
* Use JSON for all request and response bodies.
* Use JWT access tokens with refresh tokens.
* Use stable error codes that clients can handle safely.
* Use pagination for all list endpoints.
* Use ISO 8601 date and time values.
* Use idempotency keys for important write actions.

Recommended client headers:

```http
X-Client-Platform: web | android | ios
X-App-Version: 1.0.0
Idempotency-Key: <uuid>
```

`X-Client-Platform` identifies whether the request came from web, Android, or iOS.

`X-App-Version` helps troubleshoot issues from specific app versions.

`Idempotency-Key` prevents duplicate records when a client retries an important request after a timeout or weak internet connection.

Recommended idempotent actions:

* Create booking
* Upload receipt metadata
* Confirm booking
* Reject booking
* Generate billing record
* Mark billing record as paid

---

## Authentication

Protected endpoints require a JWT bearer token.

```http
Authorization: Bearer <access_token>
```

Primary roles:

* Customer
* FacilityOwner
* PlatformAdmin

---

## Login Retry and Lockout Policy

The login endpoint should protect user accounts from brute-force attempts.

Recommended policy:

| Rule | Value |
| --- | --- |
| Failed login attempts before lockout | 5 attempts |
| Lockout duration | 15 minutes |
| Failed attempt reset | After successful login |
| Lockout scope | User account plus optional IP/device tracking |
| Rate limit scope | IP address and email combination |

Behavior:

* Failed login attempts should be recorded.
* After 5 failed attempts, the account should be temporarily locked.
* While locked, login should be rejected until the lockout expires.
* Successful login should reset the failed attempt counter.
* The API should not reveal whether the email exists.
* The API should return a generic invalid login message for wrong email or password.
* The API may include `retryAfterSeconds` when the account is locked.

Recommended locked response:

```json
{
  "error": {
    "code": "AUTH_ACCOUNT_LOCKED",
    "message": "Too many failed login attempts. Please try again later.",
    "details": {
      "retryAfterSeconds": 900
    }
  }
}
```

Recommended retry headers:

```http
Retry-After: 900
```

Use rate limiting together with account lockout. Rate limiting protects the API endpoint, while account lockout protects the user account.

---

## Standard Response Shape

Successful single-resource response:

```json
{
  "data": {},
  "meta": {}
}
```

Successful list response:

```json
{
  "data": [],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 100,
    "totalPages": 5
  }
}
```

Error response:

```json
{
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "The request is invalid.",
    "details": []
  }
}
```

---

## Common HTTP Status Codes

| Status | Meaning |
| --- | --- |
| 200 | Request succeeded |
| 201 | Resource created |
| 204 | Request succeeded with no response body |
| 400 | Validation or business rule error |
| 401 | Missing or invalid authentication |
| 403 | Authenticated but not allowed |
| 404 | Resource not found |
| 409 | Conflict, such as unavailable court schedule |
| 413 | Request body is too large |
| 429 | Too many requests |
| 422 | Valid JSON but invalid business action |
| 503 | Service temporarily unavailable |
| 500 | Unexpected server error |

---

## Stable Error Codes

Web and mobile clients should not depend only on human-readable error messages because messages may change.

The API should return stable error codes that clients can safely map to UI behavior.

Recommended initial error codes:

| Code | Meaning |
| --- | --- |
| `VALIDATION_ERROR` | Request fields are missing or invalid |
| `AUTH_INVALID_CREDENTIALS` | Email or password is incorrect |
| `AUTH_ACCOUNT_LOCKED` | Account is temporarily locked because of too many failed login attempts |
| `AUTH_RATE_LIMITED` | Too many authentication requests were sent in a short time |
| `AUTH_TOKEN_EXPIRED` | Access token has expired |
| `AUTH_FORBIDDEN` | User is authenticated but not allowed |
| `RESOURCE_NOT_FOUND` | Requested record does not exist |
| `RATE_LIMIT_EXCEEDED` | Request limit was exceeded |
| `REQUEST_TOO_LARGE` | Request body exceeds the allowed size |
| `INVALID_SORT_FIELD` | Sort field is not supported |
| `INVALID_DATE_RANGE` | Date range is invalid |
| `CONCURRENCY_CONFLICT` | Another request changed or reserved the same resource |
| `DUPLICATE_REQUEST` | Same idempotent request was already processed |
| `MAINTENANCE_MODE` | API is temporarily unavailable because of maintenance |
| `BOOKING_SLOT_UNAVAILABLE` | Court schedule is no longer available |
| `BOOKING_INVALID_STATUS` | Booking action is not allowed for current status |
| `RECEIPT_REQUIRED` | Payment receipt is required |
| `RECEIPT_ALREADY_UPLOADED` | Booking already has an uploaded receipt |
| `PLATFORM_FEE_NOT_CONFIGURED` | Facility Owner has no active platform fee agreement |
| `BILLING_ALREADY_GENERATED` | Booking or period was already included in billing |
| `CONFLICT` | Request conflicts with existing data or business rules |
| `AUTH_EMAIL_ALREADY_EXISTS` | Registration email is already in use |
| `AUTH_ACCOUNT_INACTIVE` | Account has been deactivated |
| `AUTH_INVALID_REFRESH_TOKEN` | Refresh token is unknown, revoked, or expired |
| `CAPTCHA_INVALID` | reCAPTCHA verification failed |
| `AUTH_VERIFICATION_EMAIL_COOLDOWN` | Verification email was requested again too soon |
| `AUTH_INVALID_VERIFICATION_TOKEN` | Verification link is unknown or already used |
| `AUTH_EXPIRED_VERIFICATION_TOKEN` | Verification link has expired |
| `AUTH_PASSWORD_RESET_COOLDOWN` | Password reset was requested again too soon |
| `AUTH_INVALID_PASSWORD_RESET_TOKEN` | Reset link is unknown or already used |
| `AUTH_EXPIRED_PASSWORD_RESET_TOKEN` | Reset link has expired |
| `AUTH_PASSWORD_REUSED` | New password matches the current one |
| `ASSET_URL_UNTRUSTED` | Uploaded asset URL is not https on the configured Cloudinary cloud |
| `VERIFICATION_DOCUMENTS_REQUIRED` | An action needs a verification document attached |

---

## Common Query Parameters

List endpoints should support:

```text
page
pageSize
sortBy
sortDirection
search
```

Date range filters should use:

```text
dateFrom
dateTo
```

All date and time values should use ISO 8601.

---

## Rate Limiting

The API should protect public and sensitive endpoints from abuse.

Recommended initial limits:

| Endpoint Type | Recommended Limit |
| --- | --- |
| Login | Strict, per IP and email combination |
| Register | Strict, per IP |
| Public facility search | Moderate, per IP |
| Booking creation | Strict, per authenticated user |
| Receipt metadata upload | Strict, per authenticated user |
| Reports | Strict, per authenticated user and role |

When the limit is exceeded, return:

```http
429 Too Many Requests
Retry-After: 60
```

Response:

```json
{
  "error": {
    "code": "RATE_LIMIT_EXCEEDED",
    "message": "Too many requests. Please try again later.",
    "details": {
      "retryAfterSeconds": 60
    }
  }
}
```

---

## Correlation ID

Every request should have a correlation ID for troubleshooting.

Recommended header:

```http
X-Correlation-Id: uuid
```

Behavior:

* If the client sends `X-Correlation-Id`, the API should reuse it.
* If the client does not send one, the API should generate one.
* The correlation ID should be included in logs.
* Error responses may include the correlation ID in `meta` or response headers.

This helps connect frontend errors, backend logs, background jobs, and support reports.

---

## Request Size Limits

The API should reject oversized requests.

Recommended limits:

| Request Type | Recommended Limit |
| --- | --- |
| JSON request body | 1 MB |
| Receipt image upload | Upload directly to Cloudinary |
| Receipt metadata submission | Small JSON metadata only |
| Report filters | Small JSON or query string only |

The API should not receive large image files directly for booking receipts. Images should be uploaded to Cloudinary first, then the API receives only metadata.

Oversized requests should return:

```http
413 Payload Too Large
```

Error code:

```text
REQUEST_TOO_LARGE
```

---

## Pagination Limits

List endpoints must enforce pagination limits.

Recommended rules:

* Default `pageSize` is `20`.
* Maximum `pageSize` is `100`.
* Reports may use a smaller maximum depending on query cost.
* Invalid sort fields should return `INVALID_SORT_FIELD`.
* Invalid date ranges should return `INVALID_DATE_RANGE`.

---

## Time Zone Rules

Booking systems must handle date and time carefully.

Rules:

* Store timestamps in UTC in the database.
* Store each facility time zone, defaulting to `Asia/Manila`.
* Interpret court operating hours using the facility time zone.
* Accept and return ISO 8601 date and time values.
* Display booking times using the facility time zone.
* Reports should clearly define whether filters use UTC or facility local time.

**A venue's time zone is validated as it is typed** (`TimeZoneRules`). Every
hour this platform sells is a wall-clock hour at the venue — opening times, peak
windows, holidays, and "has this hour gone" are all asked on the venue's clock —
and reading that clock means resolving the stored zone. An unresolvable zone
does not fail: it quietly answers in UTC, which in Manila is eight hours out.
Nothing throws, nothing is logged, and the venue simply runs a third of a day
wrong until somebody notices by hand. So it is checked where a person can still
fix it, on both ways into a facility's details.

Both spellings are accepted, because both resolve: the IANA ids the rest of the
world uses (`Asia/Manila`) and the Windows ids a Windows host also carries
(`Singapore Standard Time`).

---

## Concurrency and Double Booking Protection

The API must prevent two customers from booking the same court slot at the same time.

Required protections:

* Use database transactions for booking creation.
* Add database constraints or conflict checks for court time slots.
* Re-check availability inside the transaction before creating the booking.
* Return `409 Conflict` when the slot is no longer available.
* Use `BOOKING_SLOT_UNAVAILABLE` or `CONCURRENCY_CONFLICT` as the error code.

The frontend should treat `409 Conflict` as a signal to refresh availability and ask the customer to choose another slot.

---

## Audit Logging

Sensitive and business-critical actions should be auditable.

Recommended audit events:

* Login failed
* Account locked
* Booking created
* Receipt uploaded
* Booking confirmed
* Booking rejected
* Platform fee agreement changed
* Billing record generated
* Billing record sent
* Billing record marked as paid
* Payment method changed
* Admin user action

Audit logs should include:

* Actor user ID
* Actor role
* Action name
* Target record ID
* Timestamp
* IP address, if available
* Correlation ID
* Before and after values for sensitive changes, when practical

---

## Maintenance Mode

The API should support maintenance mode for deployments, migrations, or emergency fixes.

When maintenance mode is active, non-essential endpoints may return:

```http
503 Service Unavailable
Retry-After: 300
```

Response:

```json
{
  "error": {
    "code": "MAINTENANCE_MODE",
    "message": "The service is temporarily unavailable. Please try again later.",
    "details": {
      "retryAfterSeconds": 300
    }
  }
}
```

Health checks and internal admin operations may remain available depending on the deployment need.

---

## Auth Endpoints

Implementation status (.NET 10): Customer and Facility Owner registration, login, rotating refresh tokens, logout, and current-user lookup are implemented. Refresh tokens are stored only as SHA-256 hashes. Login locks an account for 15 minutes after five failed attempts. Platform Administrator provisioning remains an internal/admin workflow and is not exposed as public registration.

### Register Customer

```http
POST /api/v1/auth/register/customer
```

Role:

* Public

Request:

```json
{
  "fullName": "Juan Dela Cruz",
  "email": "juan@example.com",
  "password": "StrongPassword123",
  "phoneNumber": "+639171234567"
}
```

Response:

```json
{
  "data": {
    "userId": "uuid",
    "customerId": "uuid"
  }
}
```

### Facility owner accounts

There is no self-registration endpoint for facility owners. A Platform
Administrator encodes the account from the admin console, and the owner becomes
bookable only once a contract commences. See **How a Facility Owner gets an
account** in `user-roles.md`.


### Login

```http
POST /api/v1/auth/login
```

Request:

```json
{
  "email": "juan@example.com",
  "password": "StrongPassword123",
  "captchaToken": "recaptcha-v3-token",
  "rememberMe": true
}
```

Response:

```json
{
  "data": {
    "accessToken": "jwt",
    "refreshToken": "token",
    "expiresAt": "2026-07-01T12:00:00+08:00",
    "roles": ["Customer"]
  }
}
```

Business rules:

* Failed login attempts are recorded.
* Wrong email and wrong password should return the same generic error.
* Account is temporarily locked after the configured failed attempt limit.
* Successful login resets the failed login counter.
* Login is rate limited per IP address.
* Locked responses may include `retryAfterSeconds`.
* A reCAPTCHA v3 token with the action `login` is required. Account lockout only
  protects one account at a time, so it does not stop credential stuffing, where
  one password is tried against many accounts. The captcha does.
* `rememberMe` sizes the refresh token. True issues `Jwt:RefreshTokenDays`
  (30 days); false issues `Jwt:SessionRefreshTokenHours` (12 hours), so a token
  taken from a shared computer stops working within the day.
* The access token carries a `sid` claim holding the session's refresh-token id,
  which lets the session endpoints identify the calling device.

### Refresh Access Token

```http
POST /api/v1/auth/refresh
```

Used by web and mobile clients to request a new access token without asking the user to log in again.

Request:

```json
{
  "refreshToken": "refresh-token"
}
```

Response:

```json
{
  "data": {
    "accessToken": "jwt",
    "refreshToken": "new-refresh-token",
    "expiresAt": "2026-07-01T12:00:00+08:00"
  }
}
```

### Logout

```http
POST /api/v1/auth/logout
```

Invalidates the current refresh token or session.

Request:

```json
{
  "refreshToken": "refresh-token"
}
```

### Current User

```http
GET /api/v1/auth/me
```

Role:

* Authenticated

---

### Verify Email

```http
POST /api/v1/auth/verify-email
```

Request:

```json
{
  "token": "raw-token-from-the-emailed-link"
}
```

Response:

```json
{
  "data": {
    "email": "juan@example.com",
    "verifiedAt": "2026-07-01T12:00:00+08:00",
    "alreadyVerified": false
  }
}
```

Business rules:

* Only the SHA-256 hash of the token is stored. The raw value exists only inside
  the emailed link.
* The token expires after `EmailVerification:ExpirationHours` and is single use.
* Opening an already-used link for an already-verified account succeeds with
  `alreadyVerified` true. A second click is not an error.
* An unknown or spent token returns `AUTH_INVALID_VERIFICATION_TOKEN`; an
  expired one returns `AUTH_EXPIRED_VERIFICATION_TOKEN`, so the client can offer
  to resend rather than showing a dead end.

### Resend Verification Email

```http
POST /api/v1/auth/resend-verification
```

Request:

```json
{
  "email": "juan@example.com",
  "captchaToken": "recaptcha-v3-token"
}
```

Responds `202 Accepted` with a generic message.

Business rules:

* The response never reveals whether the account exists, and is identical for an
  unknown address, an existing address, and an already-verified account.
* A cooldown of `EmailVerification:ResendCooldownSeconds` applies per account.
  Breaching it returns `429` with `retryAfterSeconds`.
* Issuing a new token deletes the account's earlier ones, so only one link is
  ever live.
* Requires a reCAPTCHA v3 token with the action `resend_verification`.

### Forgot Password

```http
POST /api/v1/auth/forgot-password
```

Request:

```json
{
  "email": "juan@example.com",
  "captchaToken": "recaptcha-v3-token"
}
```

Responds `202 Accepted` with a generic message.

Business rules:

* Same non-disclosure rule as resend: the response cannot be used to discover
  which addresses are registered.
* Cooldown of `PasswordReset:RequestCooldownSeconds` per account, returning
  `429` with `retryAfterSeconds`.
* Requires a reCAPTCHA v3 token with the action `forgot_password`. This endpoint
  sends mail, so it is both an inbox-flooding and a billing-quota vector.

### Check Password Reset Token

```http
POST /api/v1/auth/reset-password/check
```

Request:

```json
{
  "token": "raw-token-from-the-emailed-link"
}
```

Response:

```json
{
  "data": { "expiresAt": "2026-07-01T13:00:00+08:00" }
}
```

Business rules:

* Reports whether a reset link is still usable **without spending it**, so the
  reset page can show a dead link as dead on arrival instead of after the visitor
  has typed a new password.
* Shares one lookup with the reset itself, so the check and the reset can never
  disagree about what "usable" means.

### Reset Password

```http
POST /api/v1/auth/reset-password
```

Request:

```json
{
  "token": "raw-token-from-the-emailed-link",
  "newPassword": "BrandNewPassword456!"
}
```

Response:

```json
{
  "data": {
    "email": "juan@example.com",
    "revokedSessions": 3
  }
}
```

Business rules:

* The token expires after `PasswordReset:ExpirationMinutes` (60) and is single
  use. Reset links are deliberately shorter-lived than verification links.
* A successful reset revokes **every** refresh token for the account, so a stolen
  session dies with the old password, and clears any lockout.
* The new password must differ from the current one, or the request fails with
  `AUTH_PASSWORD_REUSED`. Only an exact match can be detected: the stored value
  is a one-way hash, so similarity to the old password cannot be checked.
* A rejected password does **not** mark the token used, so the same link still
  works on the next attempt.

---

## Session Endpoints

Let a signed-in user see where their account is signed in and end any of those
sessions. Every sign-in creates its own refresh token, so one account can hold
many concurrent sessions across devices.

Each endpoint identifies the calling session from the `sid` claim on the access
token, so the client never sends its refresh token back to read or manage
sessions.

### List Active Sessions

```http
GET /api/v1/auth/sessions
```

Response:

```json
{
  "data": [
    {
      "id": "guid",
      "userAgent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/128.0",
      "ipAddress": "112.201.5.11",
      "signedInAt": "2026-07-01T10:21:00+08:00",
      "lastUsedAt": "2026-07-01T12:04:00+08:00",
      "expiresAt": "2026-07-31T10:21:00+08:00",
      "isPersistent": true,
      "isCurrent": true
    }
  ]
}
```

Business rules:

* Revoked and expired sessions are excluded.
* The raw user agent is returned unparsed; the client formats it for display.
* `lastUsedAt` is stamped on every token rotation.

### Revoke One Session

```http
DELETE /api/v1/auth/sessions/{sessionId}
```

Responds `204 No Content`, or `404` when the session does not exist.

Business rules:

* The lookup filters on the caller's user id as well as the session id.
  Without that, any signed-in user could end another account's session by
  guessing a GUID.
* A session belonging to someone else returns the same `404` as a missing one,
  so the endpoint does not confirm that another user's session exists.

### Revoke Other Sessions

```http
POST /api/v1/auth/sessions/revoke-others
```

Ends every session except the caller's. Responds with `revokedSessions`.

### Revoke All Sessions

```http
POST /api/v1/auth/sessions/revoke-all
```

Ends every session including the caller's. Responds with `revokedSessions`.

Kept as its own operation rather than passing a null session id to
revoke-others: comparing a non-nullable column to `NULL` is `UNKNOWN` in SQL,
which would silently revoke nothing while reporting success.

---

### Access token window

Revoking a session invalidates its refresh token immediately, but the device's
existing access token stays valid until it expires, up to
`Jwt:AccessTokenMinutes` (15). Closing that window would mean checking `sid`
against the database on every authenticated request, turning a stateless JWT
into a database round trip per call. Shorten `Jwt:AccessTokenMinutes` if the
window needs to be tighter.

---

## Admin Endpoints

Reserved for the `PlatformAdmin` role. This is the console the platform team
operates the SaaS from.

### Onboard Facility Owner

```http
POST /api/v1/admin/facility-owners
```

Role:

* PlatformAdmin

Request:

```json
{
  "owner": { "fullName": "Juan Dela Cruz", "email": "juan@example.com", "phoneNumber": "+639171234567" },
  "business": {
    "businessName": "Abc Sports Ventures",
    "billingEmail": "billing@example.com",
    "billingPhone": "+639171234567",
    "businessRegistrationNumber": "DTI-123456"
  },
  "documents": [
    {
      "documentType": "BusinessPermit",
      "publicId": "icyplay/facility-owners/documents/permit",
      "secureUrl": "https://res.cloudinary.com/<cloud>/image/upload/v1/permit.pdf",
      "fileName": "permit.pdf",
      "contentType": "application/pdf",
      "sizeInBytes": 204800
    }
  ],
  "facility": {
    "name": "Abc Sports Center",
    "description": "Six covered courts.",
    "addressLine1": "123 Quimpo Boulevard",
    "city": "Davao City",
    "province": "Davao del Sur",
    "postalCode": "8000",
    "country": "Philippines",
    "latitude": null,
    "longitude": null,
    "timeZone": "Asia/Manila",
    "contactPhone": "+639171234567",
    "contactEmail": "hello@example.com",
    "safetyMeasures": "First aid kit on site.",
    "houseRules": "No street shoes on the court.",
    "amenityIds": ["guid"]
  },
  "operatingHours": [
    { "dayOfWeek": 1, "opensAt": "06:00:00", "closesAt": "22:00:00" }
  ],
  "contract": { "startDate": "2026-09-08", "endDate": "2027-09-08", "notes": "Signed at the Davao office." }
}
```

Response: `201 Created` with the new user id, facility owner id, facility id,
facility slug, the derived status, and whether the invitation email went out.

Business rules:

* **One transaction.** The owner account, the business profile, the documents,
  the first facility with its hours and amenities, and the contract are written
  together or not at all. An abandoned wizard leaves nothing half-built behind
  it; the browser holds the draft until the admin submits.
* **The admin never types a password.** The account is created with a random one
  nobody knows and the owner sets their own through an emailed link, so no
  human ever handles someone else's credentials.
* **The invitation is best effort.** A mail outage returns
  `invitationEmailSent: false` rather than undoing an onboarding the admin has
  already finished. The owner can request a fresh link themselves.
* **Encoding is not commencing.** The status comes back from the contract dates:
  a term starting next month leaves the owner `Pending`, and no customer sees
  the facility until a term covers today.
* Document URLs are rejected with `ASSET_URL_UNTRUSTED` unless they are https
  on the configured Cloudinary cloud. The browser posts that metadata, so it
  cannot be taken on trust.
* At least one verification document is required. An owner encoded without
  proof of who they are is the exact thing admin-led onboarding exists to
  prevent.

### List Facility Owners

```http
GET /api/v1/admin/facility-owners
```

Role:

* PlatformAdmin

Query parameters: `search`, `status`, `page`, `pageSize`, `sortBy`,
`sortDirection`. `sortBy` accepts `businessName` or `createdAt`.

Each row carries the derived status, the facility count, and the dates of the
term the status was derived from.

### List Amenities

```http
GET /api/v1/admin/amenities
```

Role:

* PlatformAdmin

The seeded lookup the onboarding wizard renders as a checklist, ordered by
category then display order.

### List Users

```http
GET /api/v1/admin/users
```

Role:

* PlatformAdmin

Query parameters: `search`, `role`, `page`, `pageSize`, `sortBy`,
`sortDirection`.

Response:

```json
{
  "data": [
    {
      "id": "guid",
      "email": "juan@example.com",
      "fullName": "Juan Dela Cruz",
      "phoneNumber": "+639171234567",
      "roles": ["Customer"],
      "isActive": true,
      "isEmailVerified": true,
      "emailVerifiedAt": "2026-07-01T12:00:00+08:00",
      "lockoutEnd": null,
      "createdAt": "2026-07-01T10:00:00+08:00"
    }
  ],
  "pagination": { "page": 1, "pageSize": 20, "totalItems": 42, "totalPages": 3 }
}
```

Business rules:

* **Read only.** Nothing on this endpoint changes an account. Suspending one or
  editing roles belongs on its own endpoint with its own audit trail, not as a
  side effect of a list.
* `sortBy` accepts `email`, `fullName` or `createdAt` only. Anything else
  returns `INVALID_SORT_FIELD` rather than silently sorting by something the
  caller did not ask for, and stops an arbitrary field reaching the query.
* `pageSize` is capped at 100, so one request cannot pull the whole user table.

---

## Catalogue Endpoints

Anonymous. The landing page is the first thing a visitor sees, and asking them
to sign in to find out whether anyone plays badminton nearby would be the wrong
way round.

The activity, court and one-court endpoints are cached for ten minutes on the
server and sent with `Cache-Control: public, max-age=5` (`CatalogFreshness`).
The server's copy is cleared on every write that could change the answer:
creating or updating a court, changing divisions, setting or lifting
maintenance, any sport lookup change, and renewing, rescheduling or cancelling a
contract.

**The browser's five seconds used to be a minute**, which was the right number
for the visitor and the wrong one for the other reader of these endpoints. An
owner who changed a rate and opened the venue's page to check it was shown the
rate they had just replaced, for up to a minute, with no way to tell that from
the save having failed — and an ordinary reload did not help, because a reload
honours this header. Only a hard refresh did, which is not something anyone
should have to know. Five seconds keeps most of what the minute was for: a burst
of clicks through the catalogue still comes off the browser, and the staleness is
now shorter than the walk from the pricing form to the page that shows it. The
real answer is an ETag, so the browser always asks and is told "unchanged" for
nothing; this is the cheap version of that.

Maintenance is on that list even though the activity list ignores it, because
the **court list carries it**. A reopened court that still reads as closed turns
customers away from a court that is free.

### List activities

```http
GET /api/v1/catalog/activities
```

The sports and events that have a court **actually configured** for them, with
counts. An activity appears only when it has an active court, in an active
facility, whose owner has a contract covering today.

Maintenance is deliberately not counted: it is temporary, and dropping a sport
because one venue is resurfacing would hide every other venue that has it.

A filter that returns nothing is worse than one that was never offered.

```json
{
  "data": [
    {
      "id": "…",
      "key": "pickleball",
      "name": "Pickleball",
      "category": "Racket sports",
      "kind": "Sport",
      "courtCount": 3,
      "facilityCount": 1
    }
  ]
}
```

`courtCount` counts every division separately: a floor marked out three ways is
three courts to book.

### List venues

```http
GET /api/v1/catalog/facilities
```

Every venue on offer, with what it has and where it is. **What the landing page
lists, because a venue is what somebody chooses first** — the site used to open
on sports, which asked a visitor to name a game before it would tell them where
anyone plays. Each venue then has a page of its own.

Not sent with a `Cache-Control` header, unlike its neighbours: this is the
listing an owner most often reloads to check a change has landed.

### List courts

```http
GET /api/v1/catalog/courts?sport=pickleball&facility={facilityId}
```

Every bookable court, or those for one sport, or those at one venue, or both.
Omit them for everything on offer — a visitor should see what is available
before being asked to choose.

`facility` is an id rather than a name: a name can be edited and two venues can
share one, and neither should change or widen what a filter matches.

A divided floor is returned **part by part**, one row per division, each
carrying the sport it is for. A court set up for three sports appears three
times at three prices, so the sport has to be on the row or the rows read as
duplicates.

Each row carries `bookableCourtId` — what a booking will be taken against —
along with the court and division name, the venue and its address and
coordinates, the cover photo, the four rates, the peak window, and whether it is
under maintenance at either level.

The rows come from the `BookableCourts` table rather than being counted out from
division numbers, so the listing, the admin console and the booking engine
cannot disagree about how many courts a venue has. See
[courts-and-pricing.md](courts-and-pricing.md#bookable-courts).

### Read one court

```http
GET /api/v1/catalog/courts/{courtId}?sport=pickleball&division=2
```

One bookable court, whole: the court, the venue around it, and what it costs.
The sport and division identify **which offering is meant**, because a court set
up for three sports is three of them at three prices.

Returns `404` when the court is not on offer for that sport, or the division
does not exist — a typed URL cannot invent a court.

The response carries the court's own gallery, the venue's gallery, amenities,
house rules, safety measures, contact details, and the opening hours already
resolved from whichever level the court follows.

**One read, not five.** A page assembled from five calls shows five different
moments, and the detail is read through the same listing that produced the card,
so the two can never disagree about the same court.

---

## Admin Court Endpoints

### List courts

```http
GET /api/v1/admin/courts?search=&facilityOwnerId=&facilityId=&page=1&pageSize=20
```

Every court on the platform. An admin correcting one should not have to remember
which venue it is in to find it. The search matches the court, the facility or
the business name.

### Create a court

```http
POST /api/v1/admin/courts
```

The whole wizard in one call. The facility is either one the owner already has
or is created here alongside the court, and either way it is a single
transaction — an abandoned wizard leaves nothing half-built.

### Read and update a court

```http
GET /api/v1/admin/courts/{courtId}
PUT /api/v1/admin/courts/{courtId}
```

`GET` reads through the same projection the facility's court list uses, so the
detail page and the list can never disagree about what closes a court.

`PUT` answers the court whole — the sports, the hours and the gallery interlock,
and saving them separately would let a court sit in a state none of the screens
meant.

### Divisions and pricing

```http
PUT /api/v1/admin/courts/{courtId}/divisions
PUT /api/v1/admin/courts/{courtId}/pricing
```

Each has its own endpoint. Re-marking a floor and renegotiating a rate are
small, frequent changes, and routing either through the whole court would put
every other field at risk to move one number.

Sports left out of either request keep what they had.

`pricing` also carries the court's peak window. A peak rate cannot be set
without one, the window must fall inside the court's opening hours, and it is
cleared when no sport charges a peak rate.

### Maintenance

```http
POST /api/v1/admin/facilities/{facilityId}/maintenance
POST /api/v1/admin/courts/{courtId}/maintenance
POST /api/v1/admin/maintenance/{periodId}/lift
```

Two levels. A facility closure closes every court in it and cannot be lifted
from a court, which the response says rather than leaving the admin looking for
a button that is not there.

---

## Admin Lookup Endpoints

```http
GET    /api/v1/admin/sports?includeRetired=false
POST   /api/v1/admin/sports
PUT    /api/v1/admin/sports/{id}
POST   /api/v1/admin/sports/{id}/retire
POST   /api/v1/admin/sports/{id}/reinstate
```

Holds events as well as sports, told apart by `kind`.

```http
GET    /api/v1/admin/holidays?includeRetired=false
POST   /api/v1/admin/holidays
PUT    /api/v1/admin/holidays/{id}
GET    /api/v1/admin/holidays/template
POST   /api/v1/admin/holidays/import
POST   /api/v1/admin/holidays/{id}/retire
POST   /api/v1/admin/holidays/{id}/reinstate
```

Retired rather than deleted in both cases: courts reference a sport, and a
booking priced as a holiday needs the day that made it one to still be there
when the receipt is questioned.

**The calendar is filled from a spreadsheet**, because the movable feasts change
every year and arrive as a proclamation listing dozens of dates at once.
Twenty-odd rows typed one at a time into a form is where the year's calendar
goes wrong.

`template` hands back an `.xlsx` with the four columns — name, date, kind,
repeats annually — the header styled, two example rows filled in, dropdowns on
the two columns with a fixed set of answers, and the date column formatted. The
fastest way to say what goes in a column is to show it filled in correctly, and
a dropdown is how a kind cannot come back misspelled.

`import` reads that file back. **The writer and the reader live in one class**
(`HolidayWorkbook`) on purpose: a template written in one place and parsed in
another drifts the first time a column is renamed, and the drift shows up as a
file that downloads cleanly and imports as nothing.

---

## Contract Endpoints

```http
POST /api/v1/admin/facility-owners/{id}/contracts
PUT  /api/v1/admin/facility-owners/{id}/contracts/{contractId}
PUT  /api/v1/admin/facility-owners/{id}/contracts/{contractId}/rates
PUT  /api/v1/admin/facility-owners/{id}/contracts/{contractId}/document
POST /api/v1/admin/facility-owners/{id}/contracts/{contractId}/cancel
```

Contracts are renewed, never rewritten: last year's term has to stay readable
beside this year's, and platform fees hang off a specific one.

The dates can be corrected. A start date typed wrong leaves an owner invisible
to customers until it comes round, which looks like a broken listing rather than
a mistyped date. Two live terms may not overlap, and a cancelled term cannot be
rescheduled — cancelling is what ends a term.

`rates` sets the platform hourly rate and the commission percentage for that
term. See [platform-fee-strategy.md](platform-fee-strategy.md) for the
arithmetic.

---

## Public Discovery Endpoints

### Search Facilities

```http
GET /api/v1/facilities?search=&city=&sportType=&page=1&pageSize=20
```

Role:

* Public

Purpose:

* Allows Customers to discover active facilities.

### Get Facility Details

```http
GET /api/v1/facilities/{facilityId}
```

Role:

* Public

### List Courts for Facility

```http
GET /api/v1/facilities/{facilityId}/courts
```

Role:

* Public

### Check Court Availability

```http
GET /api/v1/courts/{courtId}/availability?date=2026-07-01
```

Role:

* Public

Response:

```json
{
  "data": {
    "courtId": "uuid",
    "date": "2026-07-01",
    "availableSlots": [
      {
        "startsAt": "2026-07-01T18:00:00+08:00",
        "endsAt": "2026-07-01T19:00:00+08:00"
      }
    ]
  }
}
```

---

## Facility Owner Management Endpoints

### List My Facilities

```http
GET /api/v1/facility-owner/facilities
```

Role:

* FacilityOwner

### Create Facility

```http
POST /api/v1/facility-owner/facilities
```

Role:

* FacilityOwner

Request:

```json
{
  "name": "ABC Sports Center",
  "description": "Indoor courts",
  "addressLine1": "123 Main Street",
  "city": "Davao City",
  "province": "Davao del Sur",
  "country": "Philippines",
  "timeZone": "Asia/Manila"
}
```

### Update Facility

```http
PUT /api/v1/facility-owner/facilities/{facilityId}
```

Role:

* FacilityOwner

Access rule:

* Facility must belong to the authenticated Facility Owner.

### Create Court

```http
POST /api/v1/facility-owner/facilities/{facilityId}/courts
```

Role:

* FacilityOwner

Request:

```json
{
  "name": "Court A",
  "sportType": "Badminton",
  "basePrice": 300,
  "priceUnit": "PerHour"
}
```

### Update Court

```http
PUT /api/v1/facility-owner/courts/{courtId}
```

Role:

* FacilityOwner

### Configure Court Operating Hours

```http
PUT /api/v1/facility-owner/courts/{courtId}/operating-hours
```

Role:

* FacilityOwner

Request:

```json
{
  "items": [
    {
      "dayOfWeek": 1,
      "opensAt": "08:00:00",
      "closesAt": "22:00:00",
      "isClosed": false
    }
  ]
}
```

### Create Court Maintenance Block

```http
POST /api/v1/facility-owner/courts/{courtId}/maintenance-blocks
```

Role:

* FacilityOwner

---

## Payment Method Endpoints

### List My Payment Methods

```http
GET /api/v1/facility-owner/payment-methods
```

Role:

* FacilityOwner

### Create Payment Method

```http
POST /api/v1/facility-owner/payment-methods
```

Role:

* FacilityOwner

Request:

```json
{
  "methodType": "GCash",
  "displayName": "GCash QR",
  "accountName": "ABC Sports Center",
  "accountNumber": "09171234567",
  "instructions": "Pay the exact amount and upload your receipt.",
  "qrImageCloudinaryPublicId": "payment_qr/abc-sports/gcash",
  "qrImageSecureUrl": "https://res.cloudinary.com/example/image/upload/..."
}
```

### Update Payment Method

```http
PUT /api/v1/facility-owner/payment-methods/{paymentMethodId}
```

Role:

* FacilityOwner

### Set Active Payment Method

```http
POST /api/v1/facility-owner/payment-methods/{paymentMethodId}/activate
```

Role:

* FacilityOwner

---

## Booking Endpoints

### Create Booking

```http
POST /api/v1/bookings
```

Role:

* Customer

Request:

```json
{
  "courtId": "uuid",
  "startsAt": "2026-07-01T18:00:00+08:00",
  "endsAt": "2026-07-01T19:00:00+08:00"
}
```

Response:

```json
{
  "data": {
    "bookingId": "uuid",
    "bookingNumber": "BK-20260701-0001",
    "status": "PendingPayment",
    "courtRentalAmount": 300,
    "platformFeeAmount": 15,
    "totalPayableAmount": 315,
    "paymentDueAt": "2026-07-01T17:45:00+08:00",
    "paymentMethod": {
      "methodType": "GCash",
      "displayName": "GCash QR",
      "accountName": "ABC Sports Center",
      "qrImageSecureUrl": "https://res.cloudinary.com/example/image/upload/..."
    }
  }
}
```

Business rules:

* Court must be available.
* Availability must be rechecked inside a database transaction.
* Concurrent requests for the same court time slot must not create duplicate bookings.
* Platform fee must be calculated from active Facility Owner agreement.
* Booking stores rental amount, platform fee, and total payable amount.
* Booking enters `PendingPayment`.

### Get My Bookings

```http
GET /api/v1/bookings/my?page=1&pageSize=20
```

Role:

* Customer

### Get Booking Details

```http
GET /api/v1/bookings/{bookingId}
```

Role:

* Customer, FacilityOwner, PlatformAdmin

Access rules:

* Customer can view own booking.
* Facility Owner can view bookings for owned courts.
* PlatformAdmin can view all bookings.

### Upload Payment Receipt Metadata

```http
POST /api/v1/bookings/{bookingId}/receipts
```

Role:

* Customer

Request:

```json
{
  "cloudinaryPublicId": "receipts/booking-123",
  "cloudinarySecureUrl": "https://res.cloudinary.com/example/image/upload/...",
  "fileName": "receipt.jpg",
  "contentType": "image/jpeg",
  "fileSizeBytes": 340000
}
```

Response:

```json
{
  "data": {
    "receiptId": "uuid",
    "bookingStatus": "PendingVerification",
    "verificationStatus": "Uploaded"
  }
}
```

Business rules:

* Customer must own the booking.
* Booking must be `PendingPayment`.
* Receipt metadata must point to an uploaded Cloudinary asset.
* Booking moves to `PendingVerification`.

### Cancel Booking

```http
POST /api/v1/bookings/{bookingId}/cancel
```

Role:

* Customer, FacilityOwner, PlatformAdmin

Request:

```json
{
  "reason": "Customer requested cancellation."
}
```

---

## Facility Owner Booking Verification Endpoints

### List Pending Verification Bookings

```http
GET /api/v1/facility-owner/bookings/pending-verification?page=1&pageSize=20
```

Role:

* FacilityOwner

### Confirm Booking

```http
POST /api/v1/facility-owner/bookings/{bookingId}/confirm
```

Role:

* FacilityOwner

Request:

```json
{
  "note": "Receipt verified."
}
```

Business rules:

* Booking must belong to Facility Owner.
* Booking must be `PendingVerification`.
* Booking moves to `Confirmed`.
* Platform fee ledger record is created or marked billable.
* Customer notification is queued.

### Reject Booking

```http
POST /api/v1/facility-owner/bookings/{bookingId}/reject
```

Role:

* FacilityOwner

Request:

```json
{
  "reason": "Wrong payment amount."
}
```

Business rules:

* Booking must belong to Facility Owner.
* Booking must be `PendingVerification`.
* Booking moves to `Rejected`.
* Court slot may become available again.
* Platform fee is not billable by default.

---

## Platform Fee Agreement Endpoints

### Get Facility Owner Platform Fee Agreement

```http
GET /api/v1/platform-admin/facility-owners/{facilityOwnerId}/platform-fee-agreement
```

Role:

* PlatformAdmin

### Create Platform Fee Agreement

```http
POST /api/v1/platform-admin/facility-owners/{facilityOwnerId}/platform-fee-agreements
```

Role:

* PlatformAdmin

Request:

```json
{
  "feeModel": "PerHour",
  "ratePerHour": 10,
  "cashbackPercentage": 5,
  "ranges": [
    {
      "minimumDurationMinutes": 240,
      "maximumDurationMinutes": 599,
      "flatFeeAmount": 35,
      "label": "4-hour rate"
    },
    {
      "minimumDurationMinutes": 600,
      "maximumDurationMinutes": 1439,
      "flatFeeAmount": 90,
      "label": "10-hour rate"
    },
    {
      "minimumDurationMinutes": 1440,
      "maximumDurationMinutes": null,
      "flatFeeAmount": 200,
      "label": "Daily rate"
    }
  ],
  "billingCycle": "Monthly",
  "effectiveFrom": "2026-07-01T00:00:00+08:00"
}
```

Business rules:

* Only one active agreement should apply at booking time.
* Platform fee is calculated using booking duration and rate per hour.
* Range-based flat fees may override the normal per-hour calculation.
* Cashback is applied during billing, not during booking.
* Historical bookings keep the fee calculated at booking time.

---

## Billing Endpoints

### Generate Billing Report

```http
POST /api/v1/platform-admin/billing-records/generate
```

Role:

* PlatformAdmin

Request:

```json
{
  "facilityOwnerId": "uuid",
  "periodStart": "2026-07-01T00:00:00+08:00",
  "periodEnd": "2026-07-31T23:59:59+08:00",
  "billingCycle": "Monthly",
  "dueDate": "2026-08-07T23:59:59+08:00"
}
```

Business rules:

* Include confirmed billable bookings only.
* Exclude already billed bookings.
* Create billing record and line items.
* Billing status starts as `Generated`.

### Send Billing Report

```http
POST /api/v1/platform-admin/billing-records/{billingRecordId}/send
```

Role:

* PlatformAdmin

Effects:

* Billing status becomes `Sent`.
* Mailjet email notification is queued.
* Dashboard notification is created.

### Mark Billing Record as Paid

```http
POST /api/v1/platform-admin/billing-records/{billingRecordId}/payments
```

Role:

* PlatformAdmin

Request:

```json
{
  "amountPaid": 450,
  "paymentMethod": "BankTransfer",
  "paymentReference": "BANK-REF-123",
  "paidAt": "2026-08-05T10:00:00+08:00",
  "notes": "Verified manually."
}
```

### List My Billing Records

```http
GET /api/v1/facility-owner/billing-records?page=1&pageSize=20
```

Role:

* FacilityOwner

### Get Billing Record Details

```http
GET /api/v1/billing-records/{billingRecordId}
```

Role:

* FacilityOwner, PlatformAdmin

Access rules:

* Facility Owner can view own billing records.
* PlatformAdmin can view all billing records.

### Create Billing Adjustment

```http
POST /api/v1/platform-admin/billing-records/{billingRecordId}/adjustments
```

Role:

* PlatformAdmin

Request:

```json
{
  "amount": -15,
  "reason": "Approved cancellation adjustment."
}
```

---

## Notification Endpoints

### List My Notifications

```http
GET /api/v1/notifications?page=1&pageSize=20&isRead=false
```

Role:

* Authenticated

### Mark Notification as Read

```http
POST /api/v1/notifications/{notificationId}/read
```

Role:

* Authenticated

### Register Push Subscription

```http
POST /api/v1/notifications/push-subscriptions
```

Role:

* Authenticated

Version:

* Future Version 1.5

---

## Device Endpoints

Device registration prepares the API for browser push, Android push, and iOS push notifications.

### Register Device

```http
POST /api/v1/devices
```

Role:

* Authenticated

Request:

```json
{
  "platform": "web",
  "deviceName": "Chrome on Windows",
  "appVersion": "1.0.0",
  "pushProvider": "Firebase",
  "pushToken": "push-token"
}
```

Purpose:

* Links a browser or mobile device to the authenticated user.
* Stores the push token needed for future push notifications.

### Update Device

```http
PUT /api/v1/devices/{deviceId}
```

Role:

* Authenticated

Purpose:

* Updates app version, push token, or device metadata.

### Remove Device

```http
DELETE /api/v1/devices/{deviceId}
```

Role:

* Authenticated

Purpose:

* Removes a device during logout or when notifications are disabled.

---

## Report Endpoints

Report endpoints should use Dapper when SQL aggregation is clearer and faster.

### Facility Owner Booking Report

```http
GET /api/v1/facility-owner/reports/bookings?dateFrom=2026-07-01&dateTo=2026-07-31
```

Role:

* FacilityOwner

### Hours over time

Shown on the desk as **Sold Hours** — the name a venue reads. The endpoint keeps
the name of what it computes.

```http
GET /api/v1/desk/reports/hours-over-time?from=&to=&grain=Day&facilityId=
```

Roles: FacilityOwner, FacilityAttendant. No money in it, so both read the same
answer.

**The utilization report's own figures, cut by date rather than totalled per
court.** One row per court per period, carrying `openMinutes`, `soldMinutes` and
`maintenanceMinutes` — which is enough for all three ways the
page shows them: summed per period it is the venue's line, grouped by court it
is five lines, and printed as it stands it is the table.

`grain` is `Day`, `Week` or `Month`. Weeks start on a Monday, because a venue's
week does — a Sunday start would cut most weekends in half. `starts` and `ends`
are **clamped to the range asked for**, so the first and last buckets say the
days they actually cover rather than the days the week they fall in would have.

Each row also carries `parts` and `partsSold`: the parts the court is sold in,
and how many of them had a booking at some point in the period. A part booked
twice is one part sold. `parts` counts a retired part only where it sold, so
`partsSold` is never the larger.

**A period a court could not have traded in is absent, not zero.** A chart then
draws a gap where there was no offer instead of a floor where nobody bought,
which are different things and read differently to a venue.

**These are the same sums the utilization report returns**, from the same walk
of the calendar — `Utilization.Walk` is folded once into totals and once into
buckets. That is deliberate and tested: the line and the percentage sit on the
same screen, and a venue reading them together is exactly who would find them
disagreeing. Counting the days twice is how that happens.

### Court Mix

```http
GET /api/v1/desk/reports/court-mix?from=&to=&facilityId=&includeRetired=false
```

Roles: FacilityOwner, FacilityAttendant — open to attendants for now.

**What the venue has right now**: its courts by venue type (`Indoor`,
`Covered`, `Outdoor`), and the sports and events each is set up for. The range
is only for how much each venue type sold — `openMinutes` and `inUseMinutes`
are the utilization report's own figures per court, added up by roof, so the
share cannot disagree with Court utilisation.

* `summary` — courts and bookable courts on sale, how many are under a roof
  (indoor or covered), lit, and set up for events, how many kinds of event,
  and how many courts are retired.
* `venueTypes` — each type's courts, their names, and its open and sold minutes.
* `activities` — each sport and event (`kind`), on how many courts, how many
  bookable courts, and on how many it is the main sport. Sports first.
* `courts` — each court: venue type, surface, lighting, what it is set up for
  (main sport first, with divisions), bookable courts, and its own open and
  sold minutes.

**Retired courts are never counted**: they are not something the venue has any
more. `includeRetired=true` lists them in `courts`, marked `isRetired`, and
nowhere else. A year at most.

### Court Changes

```http
GET /api/v1/desk/reports/court-changes?from=&to=&facilityId=&courtId=
```

Roles: FacilityOwner, FacilityAttendant — open to attendants for now; report
visibility is a permission still to be built.

**Every change made to the venue's courts, newest first, read back out of the
audit trail.** The trail already records each one — a court's details, sports,
divisions, prices, hours and photos as the fields that changed, before and
after; its creation and maintenance as events — with who did it and the reason
given. What it stores is ids and `500.00/600.00/-/-`; this worded as a venue
reads it, on the venue's clock:

* `changes` — newest first, at most 500: `on` and `time` (the venue's), `kind`,
  `title`, `details` as `{ label, before, after }` lines (`before` null for
  something added, `after` null for something removed), `reason`, and who —
  `actorName` and `actorRole` ("Owner", "Attendant", "Platform admin"; an
  admin's own name is left out).
* `kind` is `Added`, `SportsAndDivisions`, `Prices`, `Hours`, `Maintenance`,
  `RenamedOrRetired`, `Photos` or `Details`. One saved edit can be two changes:
  a rename and a new surface read as different things.
* `kinds` — how many of each, for the filter.
* `summary` — courts and bookable courts now and what the range added and
  retired, price changes and on how many courts, closures, and closures on at
  this moment.

A venue-wide closure counts against every court. `courtId` narrows to one court
(and still shows venue-wide closures); a court at another venue answers
`NotAttended`. A year at most.

### Missed Income

```http
GET /api/v1/desk/reports/missed?from=&to=&grain=Week&facilityId=
```

Roles: FacilityOwner, FacilityAttendant — open to attendants for now, the same
as takings.

**What the venue's open, unsold hours would have earned, at its own rates.**
Built on the utilization report's walk of the calendar (the same open days,
maintenance and contract) and priced by `CourtSport.PriceAt`, the rule the
booking page sells by — an empty peak hour is worth what a customer would have
been charged for it.

* **Only hours that have begun**, on the venue's clock. One still ahead can
  still be sold, so it is not missed yet.
* **Only what could have been sold.** Hours under maintenance or when the venue
  was shut are left out, and so is any hour a clashing booking held the floor
  (confirmed or waiting on the desk).
* **A court** counts the minutes the whole floor had no booking
  (`notSoldMinutes`), and its money is its **main sport**: every part of it
  still sellable in the hour — all of them when the floor was empty, the rest
  when some were booked, none when another sport had it.
* **A sport court** (`units`) is priced on its own, at its own rate, counting
  only hours it could still have been booked. They share one floor, so they
  overlap and do not add up to the court.

`periods` is the whole venue per period, `rows` each court per period, and
`courts` each court for the range with its `units`. The range is a year at
most, as for the other reports that walk the calendar.

### Takings

```http
GET /api/v1/desk/reports/takings?from=&to=&grain=Month&facilityId=
```

Roles: FacilityOwner, FacilityAttendant. **Open to attendants for now** — who
may see money is a permission still to be built.

**What customers paid the venue, on the day the money was accepted**: a
booking's payment on the day the desk confirmed it, and an upgrade's balance on
the day the desk approved it. The confirmation day rather than the day played,
because that is when the money came in and what a venue matches against its
GCash history.

A booking's payment is `PaidTotal` less the upgrades approved since — each
upgrade adds its balance there and is counted on its own day. Not worked out
from the booking's hours as they stand: a free move to a cheaper court changes
what the hours cost, not what was paid, and there are no refunds. The platform
fee is inside every payment, per hour booked, and set apart as `platformFee`;
it is the platform's and billed to the venue later. The venue's takings are
`rental + upgrades`; what the customers paid is that plus `platformFee`.

* `periods` — every period, zero included: `bookings`, `hours`, `rental`,
  `upgrades`, `upgradeCount`, `platformFee`.
* `rows` — the same per court per period, only where money came in. A booking
  counts to the court it is on now; after an upgrade, the one it moved to.

`grain` also takes **`Quarter`, `Half` and `Year`** (calendar quarters, January
and July halves), clamped to the range like the others, and the range may be
up to **five years** (`TakingsWindowTooWide` past that). The other reports
accept the same grains and keep their one-year limit.

### Declined Bookings

```http
GET /api/v1/desk/reports/declines?from=&to=&grain=Day&facilityId=
```

Roles: FacilityOwner, FacilityAttendant. **The amounts are in it for attendants
too, for now** — it is the desk's own work being counted.

**How many payments the desk turned down, against how many it checked, and
why.** Each counts on **the day it was answered, on the venue's clock**: a
refusal on the day it was refused, a confirmation on the day it was confirmed.
The two together are `checked`, because both only happen to a payment that was
waiting — so the share of refusals is a share of every answer the desk gave.

* `periods` — every period, zero included, each with `declined`, `checked` and
  `reasons` (every reason on the list, zero included, then `null` for the
  refusals from before the list).
* `reasons` — the range's totals, most given first, leaving out a reason nobody
  gave.
* `total`, `checked` — the whole range.
* `declines` — the refusals, newest first, **at most 200**: `declinedOn`, the
  customer, venue, court, `kind`, the dates and first-to-last hour, `hours`,
  `amount` (court rental plus the platform fee — what the customer sent),
  `reason`, `note` (on an old refusal, all the desk wrote), `declinedByName`
  and `declinedByOwner`.

Upgrades the desk declined are not in it yet.

### Moved Bookings

```http
GET /api/v1/desk/reports/moves?from=&to=&grain=Day&facilityId=
```

Roles: FacilityOwner, FacilityAttendant. No money in it.

**How many bookings customers moved, and why.** A move counts on **the day it
went through, on the venue's clock** — a free move when it was asked for, an
upgrade when the desk approved it. The day the booking is for is a different
question.

* `periods` — every period in the range, zero included, each with `free`,
  `upgrade` and `reasons`. `reasons` lists every reason on the list, zero
  included and in the list's order, then `null` for the moves nobody was asked
  about. Unlike hours there are no gaps: a customer can move a booking on a day
  the venue is shut.
* `reasons` — the range's totals, most given first, leaving out a reason nobody
  gave.
* `total` — every move in the range.
* `moves` — the moves themselves, newest first, **at most 200**. The counts
  above are always the whole range. Each carries `movedOn` (the venue's day),
  the customer's name, `fromCourtName` / `toCourtName` as they were named then,
  `kind` (`Free` or `Upgrade`), `reason` and `reasonNote`.

`grain`, the one-year limit and the Monday weeks are the same as Sold Hours',
from the same code.

### Venue snapshot

```http
GET /api/v1/desk/reports/snapshot?facilityId=
```

Roles: FacilityOwner, FacilityAttendant. No money in it, so both see the same
answer — counting courts is the desk's own job.

What the venue looks like **at this moment**, on each venue's own clock:

| Figure | Means |
| --- | --- |
| `courts` | Active floors, as the venue registered them. |
| `bookableCourts` | Active parts those floors are sold in. |
| `availableNow` | No booking on them, and not closed for work. |
| `bookedNow` | Somebody is on them, on a booking that still holds the court. |
| `underMaintenanceNow` | Closed for work at this moment, booked or not. |

**The last three add up to `bookableCourts`**, and they are sorted in one order
so that they can: maintenance first, then booked, then whatever is left is
free. A part closed for work is not free whether or not somebody had it booked,
and a maintenance figure that left those out would understate the closure the
venue actually made.

An unpaid hold whose clock has run out does **not** count as booked — the same
rule `Booking.HoldsTheCourtAt` states, asked of a row. Counting it would have
the desk turning somebody away from an hour anybody can buy.

**Free is not the same as sellable**, and the page says so. A part with no
booking of its own can still be unsellable because a clashing game has the
floor: one hall booked for basketball takes its three pickleball courts with
it. The availability grid answers "can I sell this hour"; this answers "is
anybody on it".

### Court Utilization Report

```http
GET /api/v1/desk/reports/court-utilization?from=2026-09-01&to=2026-09-22&facilityId=
```

Roles: FacilityOwner, FacilityAttendant — **built on the desk, not under
`facility-owner`** as planned above. The desk is where somebody asks which
courts sit empty on a Wednesday, and the desk is owner and attendant alike.

**The money is left out for an attendant**, not hidden from them: `rental` on
the report, on each court and on each unit comes back `null`. A figure the page
declines to draw is still a figure in the payload.

Two levels, because a court is a floor and the floor is sold in parts:

| Figure | Means |
| --- | --- |
| `openMinutes` | What the court could have sold. Its own hours or the building's, less days the venue was shut, days under maintenance, and days outside the owner's contract — **rounded down to whole slots**, because the grid never offered the remainder. |
| `inUseMinutes` | Minutes the floor had somebody on it, counted **once** however many of its parts were sold for them. |
| `soldMinutes` | The parts added up. Larger than `inUseMinutes` whenever a divided floor ran two games side by side. |
| `maintenanceMinutes` | What the timetable said, on days shut for work. Kept out of `openMinutes`: nobody could have booked those hours, so they are neither used nor wasted. |
| `awaitingMinutes` | Paid for and waiting on the desk. Neither played nor lost. |
| `openDays` / `maintenanceDays` | The days behind the minutes. |
| `lastSoldOn` | On each court and each part: the last date it sold, **reaching back before the range** as far as it has to. Null when it never has. What tells a quiet month from a court nobody wants. Confirmed only — a hold that lapsed was never a sale. |

**Utilization is `inUseMinutes / openMinutes`.** Adding up the units will not
give you that, and it is not meant to: a floor marked out three ways for
pickleball can sell three o'clock three times over, and a percentage built on
that reads three hundred. The units say what the floor was used *for*, and they
add up to `soldMinutes` rather than to `inUseMinutes`.

**A retired part is still listed when it sold hours in the period**, carrying
`isRetired`. Dropping it left its hours inside the court's `soldMinutes` while
its row was gone, so the rows no longer totalled the figure above them and
nothing said what was missing. One that sold nothing is left out.

Confirmed bookings only. A booking still waiting on the desk might be turned
down, and folding it in would make a report run this afternoon disagree with
the same report run tomorrow.

The arithmetic is done in C# over an EF read rather than in SQL, and
deliberately: "was this court open, and for how long" is a rule
[booking.md](booking.md#what-is-free) already owns and the availability grid
already answers. A second copy in T-SQL would part company with the first the
day somebody let a court keep its own hours.

Refusals: `NotAttended` for a venue they do not work, `WindowBackwards`, and
`ReportWindowTooWide` past a year.

### Admin reports

```http
GET /api/v1/admin/reports/owners
GET /api/v1/admin/reports/snapshot?facilityOwnerId=&facilityId=
GET /api/v1/admin/reports/court-utilization?from=&to=&facilityOwnerId=&facilityId=
GET /api/v1/admin/reports/hours-over-time?from=&to=&grain=Day&facilityOwnerId=&facilityId=
```

Role: PlatformAdmin. **The venue desk's reports, for the platform**: across
every venue, one facility owner's, or one venue. Every figure comes from the
same code the desk runs — each report is worked out over a list of venues, and
only who decides the list differs (`IPlatformReportService`). Two copies of
"how busy is a court" would be two answers to the question the owner and the
platform most need to agree on.

* `owners` — every facility owner and their venues, for the filters.
* `snapshot` — the desk's five numbers (courts, bookable courts, available,
  booked, under maintenance) across the venues in scope as `total`, then
  `perOwner`, plus how many owners and venues are in scope. An owner with no
  venue is listed with zeros.
* `court-utilization` — the desk's Court utilisation over the venues in scope,
  **with the rental in it**: the admin sees what each owner sees of their own.
  The same limits as the desk: forwards (`WindowBackwards`) and a year at most
  (`WindowTooWide`), both 400s.
* `hours-over-time` — the desk's hours over time over the venues in scope:
  what Sold Hours draws, and the trend on Sold Courts and Not Sold Courts. An
  unknown `grain` is `UnknownGrain`, a 400.

An owner or venue that does not exist, or a venue that is not the owner's, is a
404 (`OwnerNotFound`, `VenueNotFound`) rather than a page of zeros an admin
would believe. The desk's other reports come to the admin one at a time, under
`/admin/reports/…`, with the same two filters.

### Platform Fee Billing Report

```http
GET /api/v1/platform-admin/reports/platform-fees?facilityOwnerId=uuid&dateFrom=2026-07-01&dateTo=2026-07-31
```

Role:

* PlatformAdmin

### Overdue Billing Report

```http
GET /api/v1/platform-admin/reports/billing-overdue
```

Role:

* PlatformAdmin

---

## As Built Endpoints

The sections above describe the intended surface. What follows records what the
booking and attendant work actually shipped, where the two differ.

### Availability

```http
GET /api/v1/catalog/bookable-courts/{bookableCourtId}/availability?date=2026-09-20
```

Anonymous. A visitor should see whether Saturday morning is free before being
asked to make an account; signing in is the price of holding an hour, not of
looking at one.

Returns the day hour by hour with what each costs and why — `Standard`, `Peak`,
`Weekend` or `Holiday` — and marks hours already gone. Sent `no-store`: a grid
a minute stale is a customer picking an hour that has just been taken.

### Bookings

```http
POST   /api/v1/bookings
GET    /api/v1/bookings
GET    /api/v1/bookings/{bookingId}
POST   /api/v1/bookings/{bookingId}/receipt
POST   /api/v1/bookings/{bookingId}/submit-payment
POST   /api/v1/bookings/{bookingId}/cancel
```

Roles:

* Reading your own — any signed-in account. Authorization attributes add up, so
  a role over the whole controller cannot be relaxed per action: it sits on the
  four that write instead. Reading your own record is not the privilege of the
  role that happens to make most of the records.
* Creating, attaching a receipt, submitting and cancelling — Customer.

Every read is scoped to the caller, so an account with nothing on it gets an
empty list rather than a refusal.

`POST /bookings` runs in a serializable transaction. Two people reaching for the
same hour is the ordinary case, not the rare one.

### Facility attendants

```http
GET    /api/v1/admin/facility-owners/{id}/facilities/{facilityId}/attendants
GET    /api/v1/admin/facility-owners/{id}/facilities/{facilityId}/attendants/check?email=
POST   /api/v1/admin/facility-owners/{id}/facilities/{facilityId}/attendants
POST   /api/v1/admin/facility-owners/{id}/facilities/{facilityId}/attendants/{attendantId}/resend-invitation
DELETE /api/v1/admin/facility-owners/{id}/facilities/{facilityId}/attendants/{attendantId}
```

Role: PlatformAdmin. Scoped to the owner as well as the facility, so another
owner's venue answers the same as one that does not exist.

`check` answers while the address is being typed: `Available`,
`AlreadyAttending`, `IsTheOwner` or `AlreadyRegistered`, with `canBeAdded` and
the name on the account when there is one. Only `Available` may be added — one
address, one account — and the same refusal is made again on the way in, so a
console that skipped the question gets the answer anyway.

The roster carries `invitationsSent` and `lastInvitedAt`, counted from the
invitation tokens: one for the original and one for each resend. Each resend is
also its own audit line, so how often somebody was chased is answerable later.
A resend to somebody who has already set their password is refused — a fresh
link would be an offer to replace a password they are using.

### Payment details

```http
PUT /api/v1/admin/facility-owners/{id}/payment-details
```

Role: PlatformAdmin. The GCash number, account name and QR code a customer pays
into, and how long an unpaid hold survives.

### Moving a booking

```http
GET  /api/v1/bookings/{bookingId}/move-window?date=
POST /api/v1/bookings/{bookingId}/move-options
POST /api/v1/bookings/{bookingId}/move-quote
POST /api/v1/bookings/{bookingId}/move
```

Role: Customer, and only their own booking. Another customer's answers the same
as one that is not there.

**The rules are in [booking.md](booking.md#moving-a-booking), not here.** They
are the deal rather than an implementation detail, they are shared by the move,
the quote, the search and the upgrade, and the last time they were written out
beside the endpoint the two drifted apart inside a week. What follows is the
shape of the request and what each refusal means.

A move takes a **target court and the hours to put on it**:

```json
{
  "toBookableCourtId": "…",
  "slots": [ { "date": "2026-09-25", "startsAt": "18:00" } ],
  "reason": "Weather",
  "reasonNote": null
}
```

`reason` is required on `move` and on `upgrade` — one of `ScheduleChanged`,
`Weather`, `CourtProblem`, `DifferentCourt`, `Other` — and ignored by
`move-options` and `move-quote`, which only ask. `reasonNote` is optional, up to
200 characters, and required when the reason is `Other`. The refusals are 400s:
`MoveReasonRequired`, `MoveReasonNoteRequired`, `MoveReasonNoteTooLong`. See
[booking.md](booking.md#why-it-moved).

`slots` may be omitted, which means "the same hours, on that court". A booking
sold by the day sends **dates** rather than hours to `move-options`, because its
hours are not a choice anybody made — they are whatever the court is open for,
and they cannot be named until a court is.

| Endpoint | What it answers |
| --- | --- |
| `move-window` | The hour grid for one date, with how many hours have to be picked. `Hourly` only — a day taken open to close has no hours to choose. |
| `move-options` | Every court this booking could move to, each quoted, cheapest first. |
| `move-quote` | What one named court would come to. Asks only; refuses nothing a screen is still deciding. |
| `move` | Does it. |

Runs in a serializable transaction for the same reason taking a booking does,
and the booking's own hours are excluded from what counts as taken — otherwise a
move overlapping the dates it is leaving would refuse on the strength of the very
booking being moved.

Refusals: `NotMovable`, `MoveLimitReached`, `NotTheSameOffering`,
`DayBookingInPlay`, `BookingFinished`, `KindDoesNotMatchSlots`, `SlotTaken`,
`OutsideOpeningHours`, `NotPriced`, `MoveCostsMore`, `NothingWouldChange`.

`BookingDetail` carries `movesLeft` and `canBeMoved` so a page can offer the
button or explain its absence without working the rules out again. `canBeMoved`
is answered on the server, because it is measured on the venue's clock, not the
reader's.

### Upgrading a booking

```http
POST /api/v1/bookings/{bookingId}/upgrade
GET  /api/v1/bookings/{bookingId}/upgrade
POST /api/v1/bookings/{bookingId}/upgrade/receipt
```

Role: Customer. A move onto hours that cost more, paid for and approved rather
than immediate — the one case that cannot be instant, because money has to
change hands and the venue has to see it arrive before it gives up the better
court. Rules and lifecycle: [booking.md](booking.md#upgrading-a-booking).

`POST …/upgrade` takes the same body a move does and answers the request with
its `balanceDue` and the hours it is holding. A move that costs the same or less
is not an upgrade and is refused with `NothingToUpgrade` — it should have gone
to `move`, which is free and immediate.

`GET …/upgrade` answers the one open request, or null. **One at a time**
(`MoveAlreadyRequested`): two, and the customer can be paying for hours while
the venue is approving different ones.

`POST …/upgrade/receipt` attaches the GCash receipt and hands it to the venue,
the same shape as a booking's own receipt endpoint. The hours are held until the
venue answers from that moment — the customer cannot be blamed for a queue.

### The upgrade queue

```http
GET  /api/v1/desk/upgrades?tab=Waiting&facilityId=&page=1&pageSize=10
POST /api/v1/desk/upgrades/{upgradeId}/approve
POST /api/v1/desk/upgrades/{upgradeId}/decline
```

Roles: FacilityOwner, FacilityAttendant, scoped exactly as the booking desk is.
`tab` is `Waiting` (paid, nobody has looked) or `Settled`.

`approve` re-checks rather than trusting the quote, because time has passed
since it was made: still waiting (`UpgradeNotWaiting`), a receipt attached
(`NoReceipt`), the hours still adding up (`UpgradeStale`), the target hours
still free (`UpgradeHoursTaken`). Then the booking moves at the price it was
quoted, and the balance is recorded against `PaidTotal`.

`decline` takes a reason, leaves the booking exactly where it was, and tells the
customer. Unlike a rejected booking, a declined upgrade **does** write — there
is nothing for the customer to answer, only something they need to know.

### A booking's history

```http
GET /api/v1/bookings/{bookingId}/history
GET /api/v1/desk/bookings/{bookingId}/history
```

One account of one booking, read from the platform's audit trail, and the same
answer at both doors: two readers of the same events who disagreed about what
they said would be worse than either of them being wrong. Authorisation is the
caller's — the customer's screen asks whether the booking is theirs, the desk
asks whether it is at a venue they work at.

An expiry leaves no row, because nothing is there to write one: a hold lapses by
the clock passing, not by anybody doing something. It is worked out on the way
out and added to what the trail holds.

### The venue's desk

```http
GET  /api/v1/desk/venues
GET  /api/v1/desk/bookings?tab=Waiting&facilityId=&page=1&pageSize=10
POST /api/v1/desk/bookings/{bookingId}/confirm
POST /api/v1/desk/bookings/{bookingId}/reject
```

Roles: FacilityOwner, FacilityAttendant. This replaces the planned **Facility
Owner Booking Verification Endpoints** above, which were scoped to the owner
alone.

**Which venues a caller sees is worked out from who they are**, never from a
facility id they send: an owner attends every venue they own, an attendant the
ones they are on the desk of today. `facilityId` only narrows that set, and one
outside it answers the same as one that does not exist.

`tab` is `Waiting` (paid and handed over, nobody has looked yet) or
`Confirmed`. Two piles rather than one list: what is waiting is work and what
is confirmed is a record, and three that need doing must not be buried under
fifty that are done. Waiting is ordered oldest first — somebody who paid an hour
ago should not sit behind somebody who paid a minute ago.

A row carries who booked it, because checking a GCash receipt means checking the
name on it against the person who sent it. That is what `BookingDetail` does not
have and why the desk has a shape of its own.

`confirm` refuses anything not still waiting, so a second press cannot undo the
first, and anything with no receipt on it. It emails the customer their
confirmation — the one letter that reads as one — after the save, best effort: a
mail provider being down must not undo a decision somebody has already made.

`reject` takes `{ "reason": "WrongAmount", "note": "Paid ₱4,800 only" }` — a
reason from the list in [booking.md](booking.md#status), required, and a note
that is optional except on `Other` (400s: `RejectReasonRequired`,
`RejectNoteRequired`, `RejectNoteTooLong`) — and puts the hours straight back on
sale. Its own
status rather than a cancellation: a customer changing their mind and a receipt
that did not add up read differently in a venue's history. **The customer is
emailed** (`booking-declined`) after the save, best effort, the same as a
confirmation: that the court is not held, the reason, and the venue's contact —
there is no message thread to answer from, so the letter says who to speak to
about any money sent.

### The court diary

```http
GET /api/v1/desk/courts
GET /api/v1/desk/courts/{courtId}/schedule?from=&to=
GET /api/v1/desk/courts/{courtId}/bookings?from=&to=&status=&page=&pageSize=
GET /api/v1/desk/bookings/{bookingId}
```

Roles: FacilityOwner, FacilityAttendant. Every one of these is scoped the same
way as the rest of the desk — a court at a venue the caller does not work
answers as though it were not there.

`courts` lists courts **as the venue registered them**, each with the parts it
is sold in: a floor taking basketball whole and pickleball three across is one
court with four parts. The diary is organised by the court because that is what
somebody walks onto and unlocks. Part labels are derived — the sport alone on a
whole floor, the sport and the number on a divided one — never stored, because a
stored name outlives the marking out that made it true.

`schedule` is deliberately thin: one row per booked hour, carrying the status,
the part, and who booked it. A month of one court is some five hundred hours,
and sending each one a customer's address and a receipt would be sending far
more than a calendar can draw. The rest arrives from `bookings/{id}` when
somebody clicks an hour.

It answers only for what **still holds** the hour — a hold whose clock ran out
without a receipt has let go, and an hour drawn as taken that anybody can book
sends a desk away from an hour it could have sold. What fell through is in the
list instead.

The stretch is capped at **six weeks**, not a month: a calendar's month view
reaches into the weeks either side of it, and anything wider than that is a
report rather than a diary.

`bookings` is the list, and unlike the diary it will answer for rejected,
cancelled and expired, which is what a venue comes to a list to find. Its date
filter is an **overlap**, not a start: a run of days that began before the
window is still on the court during it.

---

## Cloudinary Upload Flow

The API should not store file blobs in SQL Server.

Recommended flow:

```text
Frontend uploads file to Cloudinary
  |
  v
Cloudinary returns public ID and secure URL
  |
  v
Frontend submits metadata to API
  |
  v
API stores metadata in SQL Server
```

For tighter security, the backend may provide signed Cloudinary upload parameters.

### Create Signed Upload Parameters

```http
POST /api/v1/admin/assets/upload-signature
```

Role:

* PlatformAdmin

Request: `{ "purpose": "facility-owner-document" }`, or `"facility-photo"`.

A purpose rather than a free-text folder. The signature commits to the folder it
signs, so a caller that can name its own destination can scatter uploads
anywhere in the Cloudinary account.

Only Cloudinary's `secure_url` is ever stored. The upload response also carries
`url`, which is http and would be blocked as mixed content on an https page.

Because the browser posts the upload metadata back, the API validates it before
storing: anything whose scheme is not https, or whose host is not the configured
Cloudinary cloud, is rejected with `ASSET_URL_UNTRUSTED`. Without that check a
client could point a permit or a facility photo at any host it liked, and the
platform would render it to other users.

```http
POST /api/v1/uploads/cloudinary/signature
```

Role:

* Authenticated

Request:

```json
{
  "uploadType": "BookingReceipt"
}
```

Response:

```json
{
  "data": {
    "cloudName": "cloud-name",
    "apiKey": "api-key",
    "timestamp": 1782880000,
    "signature": "signature",
    "folder": "booking-receipts"
  }
}
```

---

## SignalR Hubs

Recommended hub:

```text
/hubs/notifications
```

Events:

* `notification.created`
* `booking.created`
* `booking.receiptUploaded`
* `booking.confirmed`
* `booking.rejected`
* `billing.generated`
* `billing.overdue`

SignalR improves realtime UX but must not be the source of truth.

---

## Authorization Rules Summary

| Action | Customer | FacilityOwner | PlatformAdmin |
| --- | --- | --- | --- |
| Search facilities/courts | Yes | Yes | Yes |
| Create booking | Yes | Optional | No |
| Upload receipt | Own booking | No | Support only |
| Confirm booking | No | Owned courts only | Support only |
| Reject booking | No | Owned courts only | Support only |
| Manage facilities/courts | No | Own only | Yes |
| Configure payment methods | No | Own only | Support only |
| Configure platform fee agreement | No | View only | Yes |
| Generate billing records | No | No | Yes |
| View billing records | No | Own only | Yes |
| Mark billing paid | No | No | Yes |

---

## Mobile Caching and Offline Notes

Mobile clients may cache read-only data to improve speed and reduce network usage.

Good candidates for caching:

* Public facilities
* Public court details
* Public court availability responses
* Customer booking history
* Notification list
* Facility Owner dashboard summaries

Write actions must always be submitted to the server.

The server remains the source of truth for:

* Court availability
* Booking status
* Payment receipt status
* Platform fee calculation
* Billing status

Mobile clients should refresh cached data after important actions such as booking creation, receipt upload, booking verification, and billing payment updates.

---

## MVP API Priority

Build these endpoint groups first:

* Auth
* Public facility/court discovery
* Facility Owner facility and court management
* Facility Owner payment methods
* Customer booking creation
* Customer receipt metadata upload
* Facility Owner booking confirmation and rejection
* Platform fee agreement management
* Billing generation and payment tracking
* Notifications
* Refresh and logout token endpoints
* Login retry and account lockout policy
* Rate limiting for public and sensitive endpoints
* Correlation ID support
* Request size and pagination limits
* Time zone rules
* Double booking protection
* Audit logging for sensitive actions
* Stable error codes
* Idempotency support for critical write endpoints
* Basic reports

Add later:

* Push subscription endpoints
* Device registration endpoints
* Maintenance mode toggle
* Export endpoints
* Dispute endpoints
* Advanced pricing endpoints
* Payment gateway endpoints

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
* `architecture.md`
