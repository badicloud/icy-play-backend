# Email templates

The HTML for IcyPlay's transactional emails. **Mailjet holds the live copy** —
these files are the source the Mailjet templates were built from, so a change
made in the dashboard should be brought back here too, or the next person edits
a version nobody is sending.

## How an email reaches somebody

1. A service builds a `TransactionalEmailMessage`: a template **key**, a
   recipient, and a bag of variables.
2. `EmailTemplateStore` looks the key up in the `EmailTemplates` table and gets
   the Mailjet **`ExternalTemplateId`** — the number from the dashboard.
3. `MailjetTransactionalEmailSender` posts that id and the variables. The HTML
   never leaves Mailjet.

So a new email needs three things: the HTML uploaded to Mailjet, its numeric id,
and a migration that seeds the row pointing one at the other.

## Who it comes from

Every letter goes out as **IcyPlay Booking <no-reply@icyplay.com>**
(`Mailjet:SenderEmail` and `Mailjet:SenderName` in `appsettings`). The API sends
the sender and the subject with every message, so they override whatever the
Mailjet template has; the **reply-to** is not sent, so the template's is used —
keep it on a mailbox somebody reads, because `no-reply@` has none.

`icyplay.com` is on Namecheap DNS and validated in Mailjet, with:

* **SPF** — `v=spf1 include:spf.mailjet.com include:spf.efwd.registrar-servers.com ~all`.
  One record for both Mailjet and Namecheap's email forwarding; a second SPF
  record would make both invalid.
* **DKIM** — `mailjet._domainkey`, added by Mailjet's automatic Namecheap setup.
* **DMARC** — `_dmarc`: `v=DMARC1; p=none; rua=mailto:icyplaybooking@gmail.com`.

It used to send as `icyplaybooking@gmail.com`. A `@gmail.com` sender cannot be
signed by Mailjet, so Gmail treats it as unauthenticated and letters land in
spam — which is why it moved. A letter stuck on **Retrying** for one recipient
while others are delivered is that recipient's mailbox (full, mistyped or
disabled), not the setup.

## Uploading one

1. Mailjet → **Transactional** → **Templates** → create, and paste the file's
   HTML into the code editor.
2. Save, then read the **template id** from the URL or the template's settings.
3. Send the id back, and the row gets seeded by migration.

Until that row exists the send fails and is **logged, not thrown** — a booking
that is already saved must not be reported as failed because a letter did not
go. So a missing template is quiet: check the logs, not the screen.

## The templates

| File | Key | Goes to | Sent when |
| --- | --- | --- | --- |
| `account-verification` | `account-verification` | A new account | Somebody registers |
| `password-reset.html` | `password-reset` | Whoever asked | A password reset is requested |
| `facility-owner-invitation.html` | `facility-owner-invitation` | A new facility owner | An admin encodes them |
| `booking-payment-received.html` | `booking-payment-received` | The customer | They upload a GCash receipt and submit it |
| `booking-payment-submitted.html` | `booking-payment-submitted` | The facility administrator | The same moment |
| `booking-confirmed.html` | `booking-confirmed` | The customer | A person at the venue has checked the payment |
| `booking-declined.html` | `booking-declined` | The customer | A person at the venue has checked the payment and turned it down |

### The three a booking sends

```text
Customer submits their receipt
  ├─ customer   "We have your payment"      partially booked, NOT confirmed
  └─ venue      "Somebody has paid"         please check it

Venue confirms
  └─ customer   "Your court is booked"      confirmed

Venue declines
  └─ customer   "Your booking was not accepted"   the court is NOT held
```

**The first letter is carefully not a confirmation.** The court is held, nothing
is confirmed, and a customer who turns up on the strength of the wrong email
finds somebody else on the court. It says so twice: in the eyebrow
(*Partially booked*) and in the sentence.

**The venue's letter goes to the owner's account email** — the person who signs
in and works the queue — rather than the billing address, which is where
invoices go and may be an accountant who has never seen a court.

## Variables

Supplied by `BookingNotifier`. A variable the template asks for and the sender
does not supply renders empty, which is why the lists below are exact.

### `booking-payment-received`

The same variables as `booking-confirmed` below, and the same values. Only the
words differ: one says the court is held, the other says it is confirmed.

### `booking-confirmed`

| Variable | Example |
| --- | --- |
| `recipient_name` | Maria Santos |
| `court_name` | Che court 1 · Pickleball 1 |
| `facility_name` | CheChe Facility |
| `sport_name` | Pickleball |
| `booking_dates` | 15 Sep 2026, or `15 Sep – 17 Sep 2026` |
| `booked_hours` | 3 |
| `rental_amount` | 1,600.00 |
| `platform_fee` | 45.00 |
| `total_amount` | 1,645.00 |
| `booking_url` | The customer's booking page |
| `support_email` | icyplaybooking@gmail.com |
| `current_year` | 2026 |

### `booking-declined`

The letter a customer most needs and least wants, so it says the three things
they will ask, in order: **the court is not held** (so they do not turn up),
**why**, and **what to do** — speak to the venue if money left their account,
because they paid the venue and IcyPlay cannot refund it; or book again, since
the hours are back on sale.

| Variable | Example |
| --- | --- |
| `recipient_name` | Maria Santos |
| `court_name` | Che court 1 · Pickleball 1 |
| `facility_name` | CheChe Facility |
| `sport_name` | Pickleball |
| `booking_dates` | 15 Sep 2026, or `15 Sep – 17 Sep 2026` |
| `booked_hours` | 3 |
| `total_amount` | 1,645.00 |
| `decline_reason` | `Wrong amount — paid ₱800 only`, or `Payment not received.` — the reason the desk picked and its note, as one sentence |
| `venue_contact` | `0917 555 0101 · desk@cheche.ph` — the venue's phone and email, or the owner's account email when it has neither |
| `booking_url` | The customer's booking page |
| `support_email` | icyplaybooking@gmail.com |
| `current_year` | 2026 |

### `booking-payment-submitted`

| Variable | Example |
| --- | --- |
| `recipient_name` | IcyPay Facility |
| `business_name` | IcyPay company |
| `customer_name` | Maria Santos |
| `customer_email` | maria@example.com |
| `court_name` | Che court 1 · Pickleball 1 |
| `facility_name` | CheChe Facility |
| `sport_name` | Pickleball |
| `booking_dates` | 15 Sep 2026 |
| `booked_hours` | 3 |
| `total_amount` | 1,645.00 |
| `receipt_url` | The Cloudinary link to the GCash screenshot |
| `booking_url` | The booking page |
| `support_email` | icyplaybooking@gmail.com |
| `current_year` | 2026 |

Amounts arrive **formatted, without a currency symbol** — `1,645.00`. The peso
sign is in the HTML, because a template handed `1645` has no way to know what
it is looking at.

## Why the HTML looks like that

Tables and inline styles everywhere, which is not how anyone writes a web page
any more. Outlook renders with Word's engine: no flexbox, no grid, and a
stripped `<style>` block. An email built the modern way renders beautifully in
Gmail and falls apart for whoever opens it in Outlook — and the venue
administrator reading the second template is exactly the person likely to.
