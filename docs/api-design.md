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

### Court Utilization Report

```http
GET /api/v1/facility-owner/reports/court-utilization?dateFrom=2026-07-01&dateTo=2026-07-31
```

Role:

* FacilityOwner

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
