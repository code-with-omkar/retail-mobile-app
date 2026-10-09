# Mini-plan: online payment with Razorpay (P7)

Status: **backend and app side built and tested (2026-10-09); migration applied to the dev database; online payment is off until Razorpay test keys are put in server configuration.** What is still open: the real-provider live check on the phone (needs the test keys and a public webhook address), the security/PCI note (7.8) and the staff payments view (7.7, portal side).

## Why

Today every order is cash on delivery. Customers without cash, and the shop's wish to be paid before packing, need online payment: UPI, cards and netbanking. Money paths need more care than anything built so far, so the plan is explicit about how a payment can go wrong (double charge, tampered amount, payment arriving after the hold expired, the app closing mid-payment) and what the server does each time.

## Decisions

| # | Decision |
| --- | --- |
| D9 | Provider: **Razorpay**, using its hosted checkout, so card and UPI details never reach our app or server. |
| D48 | Customers choose **Pay online or Cash on delivery** at checkout. Cash on delivery works exactly as today. |
| D49 | **The order is created first** as "awaiting payment" and its items are **held for 15 minutes** (stock is taken at once, as for any order). If payment succeeds the order becomes a normal pending order; if payment fails or the 15 minutes pass, the hold is released and the order is cancelled. The customer can retry payment inside the 15 minutes. |
| D50 | A **paid order that is cancelled** (by the customer while the shop has not accepted it) **or declined** (by the shop) is **refunded in full automatically** to the original payment method, and the customer is notified. Refund status shows in the app and in the admin portal. |
| D51 | The shop **does not see an online order until it is paid.** Orders awaiting payment are hidden from staff. Cash on delivery orders appear at once, as today. |

## Today

- **Orders** have a status (Pending, Accepted, Preparing, Ready, Completed, Rejected, Cancelled), a payment method that is always `CashOnDelivery`, and a total the server calculates.
- **Checkout** takes stock and creates the order in one transaction, with an idempotency key; **cancel** returns the stock exactly once.
- **App:** the payment section shows only Cash on delivery.
- **Nothing** records a payment, talks to a gateway, or runs in the background on the server.

## Design

### The payment flow

1. Checkout with `paymentMethod: Online` creates the order with status **AwaitingPayment** (stock held, `PaymentExpiresAt` = now + 15 minutes) and a **Payment** record (status Created). The same idempotency key as today protects against a double tap.
2. The app asks the server to **start the payment**: the server creates a Razorpay order for the amount **it calculated** (never one sent by the app) and returns the Razorpay order id and the public key id.
3. The app opens Razorpay's checkout. The customer pays there.
4. On success Razorpay gives the app a payment id and a **signature**. The app sends them to the server; the server checks the signature with the secret key and marks the payment **Paid** and the order **Pending** (visible to the shop). Repeating this call changes nothing.
5. **Independently, Razorpay's webhook** tells the server the same (`payment.captured`, `payment.failed`, `refund.processed`). The webhook is signed; the server verifies the signature, ignores events it has already handled (by event id), and applies the same transitions. This is what makes the order correct even if the app was closed or lost connection after paying.
6. A **background job** runs every minute: it cancels orders still awaiting payment past their 15 minutes and returns their stock. If a payment for such an order arrives late, the server **refunds it automatically** and tells the customer.
7. A **reconciliation job** asks Razorpay about payments that have been Created or Refunding for a while and lists any that disagree with our records (for the admin portal and the logs).

### Database (additive)

- `Payments`: `Id`, `OrderId`, `Provider`, `ProviderOrderId`, `ProviderPaymentId`, `AmountPaise`, `Currency`, `Status` (Created, Paid, Failed, Refunding, Refunded, RefundFailed), `Attempts`, `RefundId`, `CreatedAt`, `UpdatedAt`. Unique on the provider order id and on the provider payment id.
- `PaymentEvents`: the webhook events already handled (provider event id unique), so a replay is harmless.
- `Orders`: `PaymentStatus` and `PaymentExpiresAt`; the status list gains **AwaitingPayment**. Existing orders are all cash on delivery and keep working.
- Same migration rules as before: schema only, reviewed, disposable database first, a verified backup and your go-ahead before the dev database.

### API (all amounts decided on the server)

- **Checkout:** `paymentMethod` (`CashOnDelivery` default, or `Online`).
- `POST api/customer/orders/{id}/payment`: starts or restarts the payment inside the hold (not after it).
- `POST api/customer/orders/{id}/payment/confirm`: payment id and signature from the app.
- `POST api/payments/razorpay/webhook`: anonymous but signature-verified; always answers quickly.
- Order answers gain `paymentStatus`, `paymentExpiresAt`, `refundStatus`.
- **Cancel / decline** of a paid order starts the refund in the same step.
- Staff views do not list AwaitingPayment orders; a payments list for the admin portal (task 7.7).

### Secrets and safety

- The key secret and the webhook secret live **only in server configuration** (environment variables or user secrets), never in the app, the repository or logs. The public key id is not secret and goes to the app in the payment-start answer.
- The repository guard test is extended to fail on a Razorpay secret in source.
- Logs record order and payment ids and statuses, never card data, signatures or secrets.
- **PCI:** the hosted checkout keeps card data off our systems; a short written scope statement is part of the phase (7.8).

### Mobile

- Checkout shows **Pay online** and **Cash on delivery** (Cash is the default, as today).
- Pay online: the order is created, the Razorpay screen opens, and the result is sent to the server. Success goes to the order success screen; failure or cancel shows "Payment not completed" with **Try again** and the time left of the 15 minutes.
- **Closing the app or losing connection mid-payment:** the order screen shows "Awaiting payment" with the countdown and a **Complete payment** button; if the webhook already settled it, it shows as paid.
- Orders list and detail show payment status and refund status ("Refund of Rs X on its way"). Messages in English and Marathi.
- Android needs the Razorpay plugin (`razorpay_flutter`); the key id comes from the server, not from the app's files.

## Out of scope

- Saved cards or saved UPI, wallets as a stored balance, EMI, pay-later, coupons, tips.
- Partial refunds, refunds after the shop has accepted (the customer calls the store, as today), chargeback handling.
- Payout and settlement reports.

## What I need from you

Building and testing do **not** need anything: I build behind a payment-gateway interface with a fake gateway, and test every path (success, failure, tampered amount, wrong signature, duplicate webhook, late payment, expiry, refund) without Razorpay.

For the live checks at the end I need, **only when we get there:**
1. A Razorpay account in **test mode** (no business verification needed for test mode).
2. The test **Key Id**, **Key Secret** and a **webhook secret** you choose. You put the secret values into the API's configuration yourself (I will tell you where); I never need to see them.
3. For the webhook to reach your PC during development, a public tunnel (for example Cloudflare Tunnel or ngrok). Without it, the confirm call from the app and the reconciliation job still settle payments, and the webhook is tested with signed test requests.

## Impact on consumers

| Consumer | Impact |
| --- | --- |
| Customer app (current build) | Keeps working: new fields are extra and cash on delivery is unchanged. |
| Admin portal | AwaitingPayment orders never appear. A later task (7.7) adds payment and refund visibility; until then the portal shows paid orders like any other. |
| Existing orders | Unchanged. |

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Tampered amount | The amount is calculated and stored on the server; the app never sends one; the paid amount from Razorpay must equal it or the payment is flagged and refunded. |
| Forged payment result | The signature is verified with the secret key; the webhook is verified with its own secret. |
| Double charge or double confirmation | Idempotent confirm; webhook events recorded by id; one payment per Razorpay order; parallel-call tests on SQL Server. |
| Payment after the hold expired | Auto-refund and a notification. |
| App closed after paying | The webhook (and the reconciliation job) settle the order without the app. |
| Stock held and never released | The expiry job runs every minute and is safe to run twice; tested including the race with a payment arriving at that moment. |
| Refund fails | Status RefundFailed, retried by the reconciliation job, shown in the portal. |
| Secrets leak | Server configuration only, guard test, no secrets in logs. |

## Tasks

| Task | What |
| --- | --- |
| 7.1 | Decisions made (this plan); test account and keys when we reach the live checks |
| 7.2 | Migration: Payments, PaymentEvents, order payment fields, AwaitingPayment status |
| 7.3 | API: start payment (amount from the server), confirm with signature, checkout with `paymentMethod` |
| 7.4 | API: signed idempotent webhook, reconciliation job |
| 7.5 | Stock hold: 15-minute expiry job, auto-release, late-payment refund |
| 7.7 | Refunds on cancel and decline; refund status; payments list for the portal |
| 7.9 | Tests: webhook replay, amount tampering, double submit, signature failures, expiry race (SQL Server) |
| 7.6 | App: payment choice, Razorpay checkout, retry with countdown, resume after closing, statuses (EN and Marathi) |
| 7.8 | Security review: PCI scope statement, secrets, log redaction, guard test |

## Proposed order of work

1. Your go-ahead.
2. Backend on a disposable database with a fake gateway and all the tests above (7.2 to 7.5, 7.7, 7.9, part of 7.8); then backup, apply to the dev database.
3. Razorpay implementation of the gateway and the live checks with your test keys.
4. App (7.6) with widget tests; check on the phone with test payments (including a failed one and closing the app mid-payment).

## Rollback

The migration's Down drops the new tables and columns. Cash on delivery is untouched, so turning online payment off is a configuration switch (`Payments:Enabled`, default off until you turn it on) that hides the option in the app.
