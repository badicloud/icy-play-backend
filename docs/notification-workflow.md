# Notification Workflow

## Purpose

This document defines how notifications should work across the Sports Facility Booking & Management Platform.

Notifications are important because Facility Owners need to know when a Customer creates a booking or uploads a payment receipt, Customers need booking status updates, and Platform Administrators need operational and billing visibility.

---

## Notification Types

The platform can support several notification channels.

Recommended channels:

* In-app realtime notifications
* Email notifications
* Browser push notifications through Progressive Web App (PWA)
* SMS notifications as a future paid option
* Native mobile push notifications as a future option

Notifications should be treated as supporting communication, not as the source of truth. Booking status, payment verification status, and billing status must always come from persisted database records.

---

## Channel Summary

| Channel | Technology | Works when app is open | Works when app is closed | MVP Priority |
| --- | --- | --- | --- | --- |
| In-app realtime | SignalR / WebSocket | Yes | No | Version 1 |
| Email | Mailjet | Yes | Yes | Version 1 |
| Browser push | PWA + Firebase Cloud Messaging | Yes | Yes | Version 1.5 |
| SMS | SMS provider | Yes | Yes | Future |
| Native mobile push | Firebase Cloud Messaging / APNS | Yes | Yes | Future |

---

## In-App Realtime Notifications

In-app realtime notifications are shown while the user is actively using the web app.

Recommended technology:

* SignalR
* WebSocket fallback support through ASP.NET Core hosting

Example:

```text
Facility Owner has dashboard open
  |
  v
Customer creates booking
  |
  v
SignalR sends realtime event
  |
  v
Facility Owner sees "New Booking Received"
```

Limitations:

* Works best when the web app is open.
* Does not reliably notify users if the browser is closed.
* Should not be the only notification channel for important booking events.

Use SignalR for:

* New booking alerts
* Payment receipt uploaded alerts
* Booking confirmed alerts
* Booking rejected alerts
* Booking expired alerts
* Live dashboard updates

In-app notifications should also be persisted in the database so users can see unread notification history even if the realtime event was missed.

---

## Email Notifications

Email notifications are reliable and should be included in Version 1.

Recommended technology:

* Mailjet
* Hangfire for retry and background sending

Email should be used for important events that users may miss if they are not currently online.

Use email for:

* Booking created
* Payment receipt uploaded
* Booking confirmed
* Booking rejected
* Booking cancelled
* Booking expired
* Payment verification reminder
* Daily booking summary
* Weekly or monthly report generated
* Platform fee billing report generated
* Platform invoice generated
* Platform invoice due reminder
* Platform invoice overdue reminder

Example:

```text
Customer uploads receipt
  |
  v
Booking status becomes Pending Verification
  |
  v
Background job sends email
  |
  v
Facility Owner receives verification email
```

Email is the safest initial notification channel for the MVP.

---

## Browser Push Notifications

Browser push notifications should be added after the MVP, once the web app is stable.

Recommended approach:

* Convert the Next.js web app into a Progressive Web App (PWA).
* Use Firebase Cloud Messaging (FCM) for browser push notifications.
* Ask users to allow notifications.
* Store user notification tokens securely.
* Send push notifications from the backend or background worker.

This allows users to receive notifications even when the web app is not actively open, depending on browser and operating system support.

Example:

```text
Customer creates booking
  |
  v
Booking is saved to database
  |
  v
Background worker prepares notification
  |
  v
Firebase Cloud Messaging
  |
  v
Chrome / Android / Windows notification
  |
  v
Facility Owner receives "New Booking" alert
```

Example push notification content:

```text
New Booking
Court A
6:00 PM
Juan Dela Cruz
```

Benefits:

* Works with the web app.
* Can feel similar to a native mobile app.
* Can be installable on Android and Windows.
* Reduces the need to build an Android app early.
* Keeps one main codebase during the startup stage.

Limitations:

* Requires user permission.
* Browser and operating system support can vary.
* iOS support has limitations compared with Android.
* Requires service worker and PWA setup.

---

## SMS Notifications

SMS notifications are useful but should not be part of the first MVP unless required by actual customers.

Reasons to delay SMS:

* SMS has a cost per message.
* Phone number validation is required.
* Failed delivery handling is needed.
* Abuse and spam controls are needed.

Possible SMS use cases:

* New booking alert for Facility Owner
* Urgent booking verification reminder
* Booking confirmed notification for Customer
* Booking cancelled notification
* Platform fee billing reminder

SMS can be added later as a paid add-on or premium notification feature.

---

## Native Mobile Push Notifications

Native mobile push notifications are a future option if the product eventually needs Android or iOS apps.

Do not build a native mobile app during the MVP unless there is a strong business requirement.

Recommended path:

```text
Version 1
  |
  v
Email + Dashboard + SignalR
  |
  v
Version 1.5
  |
  v
PWA + Browser Push Notifications
  |
  v
Version 2
  |
  v
Android / iOS app if needed
```

For the startup stage, a PWA is more cost-effective than maintaining separate native apps.

---

## Recommended Roadmap

### Version 1

Use reliable and simple notification channels.

Included:

* Dashboard notifications
* SignalR in-app realtime notifications
* Mailjet email notifications
* Hangfire background email sending

### Version 1.5

Add PWA and browser push notifications.

Included:

* Next.js PWA setup
* Service worker
* Firebase Cloud Messaging
* Browser notification permission flow
* Notification token storage
* Push notification sending through backend jobs

### Version 2

Consider native mobile app support only if the platform needs deeper mobile features.

Possible additions:

* Android app
* iOS app
* Native push notifications
* Device-specific notification preferences

### Version 3

Consider paid integrations and advanced channels.

Possible additions:

* SMS notifications
* WhatsApp or chat integrations
* PayMongo or Xendit payment notifications
* Advanced notification automation

---

## Core Booking Notification Events

### Booking Created

Triggered when a Customer creates a booking request.

Recipients:

* Facility Owner
* Customer

Channels:

* SignalR
* Email
* Browser push in Version 1.5

### Payment Receipt Uploaded

Triggered when a Customer uploads a payment receipt.

Recipients:

* Facility Owner
* Customer

Channels:

* SignalR
* Email
* Browser push in Version 1.5

### Booking Confirmed

Triggered when a Facility Owner verifies payment and confirms the booking.

Recipients:

* Customer

Channels:

* SignalR
* Email
* Browser push in Version 1.5

### Booking Rejected

Triggered when a Facility Owner rejects the uploaded payment receipt or booking request.

Recipients:

* Customer

Channels:

* SignalR
* Email
* Browser push in Version 1.5

### Booking Cancelled

Triggered when a booking is cancelled by an allowed actor.

Recipients:

* Customer
* Facility Owner

Channels:

* SignalR
* Email
* Browser push in Version 1.5

### Platform Fee Billing Report Generated

Triggered when the Platform generates a platform fee billing report for a Facility Owner.

Recipients:

* Facility Owner
* Platform Administrator, optional

Channels:

* Email
* Dashboard notification

### Platform Fee Billing Due

Triggered before or on the due date for a platform fee billing cycle.

Recipients:

* Facility Owner

Channels:

* Email
* Dashboard notification
* Browser push in Version 1.5

### Platform Fee Billing Overdue

Triggered when a platform fee billing cycle is unpaid after the due date.

Recipients:

* Facility Owner
* Platform Administrator

Channels:

* Email
* Dashboard notification
* Browser push in Version 1.5

### Platform Invoice Generated

Triggered when the Platform generates an invoice for a Facility Owner.

Recipients:

* Facility Owner
* Platform Administrator

Channels:

* Email
* Dashboard notification

---

## Notification Architecture

Recommended flow:

```text
Business event occurs
  |
  v
Database transaction completes
  |
  v
Notification record is created
  |
  v
Notification delivery jobs are queued
  |
  v
Hangfire background job processes event
  |
  +--> SignalR for in-app notifications
  |
  +--> Mailjet for email notifications
  |
  +--> Firebase Cloud Messaging for browser push notifications
```

Notifications should usually be sent after the main business transaction succeeds. This avoids notifying users about actions that failed to persist.

Recommended internal notification records:

* Notification ID
* Recipient user ID
* Recipient role
* Notification type
* Title
* Message
* Target URL or target entity
* Read or unread status
* Created timestamp
* Read timestamp

Recommended delivery records:

* Notification ID
* Channel
* Delivery status
* Attempt count
* Last attempted timestamp
* Delivered timestamp
* Failure reason

Delivery statuses:

* Pending
* Sent
* Failed
* Cancelled

Email and push notification sending should be retried through Hangfire when failures are temporary.

---

## Notification Preferences

Users should eventually be able to configure notification preferences.

Possible preferences:

* Email enabled or disabled
* Browser push enabled or disabled
* Booking alerts enabled or disabled
* Report emails enabled or disabled
* Platform fee billing emails enabled or disabled
* Invoice emails enabled or disabled
* Quiet hours

For MVP, notification preferences can be simple and role-based.

Recommended MVP defaults:

* Customers receive booking status emails.
* Facility Owners receive booking, receipt, and billing emails.
* Platform Administrators receive platform fee billing and operational alerts.
* SignalR dashboard notifications are enabled for authenticated users.

---

## Reminder Rules

Reminder notifications help prevent bookings and billing from staying pending too long.

Recommended MVP reminders:

* Remind Customer when booking is still `Pending Payment` near expiration.
* Remind Facility Owner when booking is still `Pending Verification`.
* Remind Facility Owner when platform fee billing is due.
* Remind Facility Owner and Platform Administrator when platform fee billing is overdue.

Reminder timing should be configurable later.

---

## Security and Privacy

Notification content should avoid exposing sensitive data unnecessarily.

Rules:

* Do not include payment receipt image URLs directly in public notification content.
* Do not include sensitive customer details in browser push previews unless necessary.
* Require authentication before opening notification target pages.
* Store FCM tokens securely.
* Allow users to revoke push notification access.
* Audit important notification failures for operational support.
* Do not expose platform fee billing totals in public push previews unless the user has opted in.
* Use short push notification text and send users to the authenticated app for details.

---

## MVP Requirements

The MVP notification workflow should include:

* Persistent in-app notification records
* SignalR realtime delivery while users are online
* Mailjet email delivery for important booking and billing events
* Hangfire background jobs for sending and retrying notifications
* Booking created notification
* Receipt uploaded notification
* Booking confirmed notification
* Booking rejected notification
* Booking cancelled notification
* Booking expired notification
* Platform fee billing generated notification
* Basic unread/read state for dashboard notifications

PWA browser push notifications should be planned for Version 1.5, after the MVP booking and payment flow is stable.

---

## Related Documents

* `overview.md`
* `business-model.md`
* `user-roles.md`
* `architecture.md`
* `booking-workflow.md`
* `payment-workflow.md`
* `billing-workflow.md`
* `roadmap.md`
