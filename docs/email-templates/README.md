# Mailjet transactional email templates

## Email verification

- Suggested Mailjet template name: `IcyPlay - Verify Email`
- Suggested subject: `Verify your IcyPlay email address`
- Published Mailjet template ID: `8278054`
- HTML part: `email-verification.html`
- Text part: `email-verification.txt`
- Mailjet Send API version: `v3.1`
- The send request must set `TemplateLanguage` to `true`.

### Required variables

| Variable | Description | Example |
| --- | --- | --- |
| `recipient_name` | Registered user's display name | `Juan Dela Cruz` |
| `verification_url` | Absolute, single-use frontend verification URL | `https://app.example.com/verify-email?token=...` |
| `expiration_hours` | Token lifetime displayed to the user | `24` |
| `support_email` | Public support email address | `support@example.com` |
| `current_year` | Four-digit year | `2026` |

`verification_url`, `support_email`, and `current_year` must always be supplied. `recipient_name` and `expiration_hours` have display defaults, but the backend should still provide them for predictable rendering.

The HTML template uses the combined IcyPlay email header hosted on Cloudinary:

```text
https://res.cloudinary.com/dotoabiyj/image/upload/v1786979140/IcyPlay%20Booking%20And%20E-commerce/icyplay_email_header_tiovbl.png
```

The backend does not need to send a `logo_url` variable or attach a separate image.

The `AddEmailTemplates` migration seeds this template as the active `account-verification` template. API keys and secrets must remain in secure configuration and must never be stored in the email-template database table.

## Local Mailjet configuration

`backend/IcyPlay.Api/appsettings.Development.json` is ignored by Git and contains the local-only `Mailjet` configuration section:

```json
{
  "Mailjet": {
    "ApiKey": "<rotated-api-key>",
    "ApiSecret": "<rotated-api-secret>",
    "SenderEmail": "icyplaybooking@gmail.com",
    "SenderName": "IcyPlay"
  }
}
```

Never commit credentials. Rotate any credential that has been pasted into chat, logs, screenshots, tickets, or source control before using it.

For deployed environments, provide the same values through the platform secret manager or environment variables:

- `Mailjet__ApiKey`
- `Mailjet__ApiSecret`
- `Mailjet__SenderEmail`
- `Mailjet__SenderName`

## Account-verification workflow configuration

Customer registration creates a cryptographically random, single-use verification token. Only its SHA-256 hash is stored in `EmailVerificationTokens`; the raw token is included exclusively in the email verification URL.

Configure the verification link and display values through `EmailVerification`:

```json
{
  "EmailVerification": {
    "VerificationUrl": "https://app.example.com/verify-email",
    "ExpirationHours": 24,
    "SupportEmail": "support@example.com"
  }
}
```

Deployment environment-variable equivalents:

- `EmailVerification__VerificationUrl`
- `EmailVerification__ExpirationHours`
- `EmailVerification__SupportEmail`

If Mailjet rejects the message during customer registration, the customer, profile, role, and verification token transaction is rolled back so the user can retry registration safely.
