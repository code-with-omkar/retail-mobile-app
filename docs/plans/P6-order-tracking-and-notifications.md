# Mini-plan: order tracking and notifications (P6)

Status: **decisions D43 to D47 accepted as recommended on 2026-10-09. Backend and app built and tested 2026-10-09.** Backend applied to the dev database after a verified backup (backup retail-mobile-app_pre-P6-orders; 17 migrations; 28 live checks passed). App: 390 tests, plus live smoke tests against the dev API (accounts, orders and stock cleaned up afterwards). Waiting for a check on the phone, including a status change made by staff while the app is open (task 6.7). Push notifications (6.5) stay out of this phase (D44).

## Why

After P5 a customer can place a real order and read it back, but they have to open the Orders tab and pull to refresh to see whether the shop has accepted it. The server already writes a notification each time staff change an order's status, but the app never shows them. And a customer who changes their mind has no way to cancel. P6 closes those gaps: the order screen updates by itself while it is open, notifications have a list and a bell with a count, the order shows the store and an arrival estimate, and a customer can cancel an order the shop has not accepted yet.

## Decisions

My recommendation is first in each row.

| # | Decision | Recommendation |
| --- | --- | --- |
| D43 | **How the app learns about a status change.** | **Polling while the app is open**: the order screen every 15 seconds, the Orders list every 30 seconds while any order is still active, the notification count every 60 seconds on Home. It slows down after repeated failures, stops when an order is finished, and does nothing when the app is in the background. No new server technology. Real-time (SignalR) is a later option if polling feels slow. |
| D44 | **Push notifications when the app is closed (FCM, decision D10).** | **Not in P6.** It needs a Firebase project, a device-token table and sending code, and is a good phase of its own. P6 makes notifications work inside the app; the server already records them, so push can be added on top without changing them. |
| D45 | **Cancelling an order.** | A customer can cancel **only while the order is still waiting for the shop to accept it**. The stock goes back, the shop sees it as cancelled, the customer gets a notification. There is nothing to refund because payment is cash on delivery. After the shop accepts, the customer calls the store (the store's phone is shown on the order). |
| D46 | **What the order shows beyond today.** | The **store name and phone** and the **arrival estimate given when the order was placed** (kept on the order, so it does not change if the store's estimate changes later). Staff are not asked for anything new. |
| D47 | **Notifications screen.** | A list (newest first, unread marked), tap opens the order and marks it read, "Mark all as read", a bell on Home with the unread count. |

## Today

- **Status flow on the server:** Pending → Accepted → Preparing → Ready → Completed, or Pending → Rejected. The app shows these as Order placed, Packed, Out for delivery, Delivered, Declined. The server has statuses for "out for delivery", "delivered" and "cancelled" in its list but does not let staff move an order into them yet.
- **Server notifications:** one is created on every status change (`api/customer/notifications`, mark one read). There is no "mark all", no unread count, and the app does not read them.
- **Order answer:** has the lines, status history and fees (P5), but only the store's id, not its name, and no estimate.
- **App:** the Orders tab and the order screen read once and refresh on pull.

## Design

### Database (additive, one column)

`Orders.EstimatedDeliveryMinutes` (nullable int): the store's estimate when the order was placed. Old orders have none, and the app then shows no estimate for them. Same migration rules as before: schema only, Up and Down reviewed, disposable database first, then a verified backup and your go-ahead before the dev database.

### API

- **Order answers** (`OrderResponse`) gain `storeName`, `storePhone` (if the store has one; see below) and `estimatedDeliveryMinutes`. Additive, so the current app keeps working.
- **`POST api/customer/orders/{id}/cancel`**: only the owner (anyone else gets 404). Allowed only while the status is Pending; otherwise **409** with `reason` `OrderNotCancellable`. In one transaction: status becomes Cancelled with a history row, the stock of every line goes back, a notification "Your order was cancelled" is written. Calling it twice is safe (the second answer is the same cancelled order, 200). The staff status route and its transition rules do not change, except that staff can no longer accept an order the customer has just cancelled (it is no longer Pending, so the existing rule already refuses).
- **Notifications:** `GET api/customer/notifications/unread-count` (a number, cheap enough to poll) and `PUT api/customer/notifications/read-all`.
- Staff-facing change: the admin portal lists orders with a status; a Cancelled order will show as such. I will check that the portal handles the status before this is applied, and tell you if it needs a change.

### Mobile

- **Live status:** one polling helper (interval, backoff on failure, stops at a finished order, paused when the app is not in front, resumed on return). The order screen shows the timeline moving and a small "updated just now" line. If the network drops it keeps the last data and says it could not refresh.
- **Order screen:** store name with a call button, "Arriving in about N minutes" while the order is active, a **Cancel order** button only while it is waiting for the shop (with a confirmation), and a clear "Cancelled" and "Declined" end state.
- **Orders list:** active orders first with their live status, finished ones below.
- **Notifications:** the bell on Home with the unread count, a Notifications screen (list, tap to open the order, mark all read), empty and error states, English and Marathi.
- **Reorder / Buy again (6.6):** already works with pack sizes and reports what was left out; P6 adds the missing tests.

## Out of scope

- Push notifications when the app is closed (D44), real-time connections, SMS or WhatsApp.
- Partial cancellation, cancelling after acceptance, refunds (P7 with online payment).
- Rider tracking on a map, delivery OTP, order rating.
- Changing the staff status rules, or the admin portal beyond showing "Cancelled".

## Impact on consumers

| Consumer | Impact |
| --- | --- |
| Customer app (current build) | Keeps working: new fields are extra and the new routes are new. |
| Admin portal | A customer can now cancel a pending order, so it can see a Cancelled status on an order it has not touched. I will check how the portal shows it first. |
| Existing orders | Unchanged; they simply have no estimate. |

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Cancel races with the shop accepting at the same moment | One transaction with the order's concurrency token: exactly one of "accepted" and "cancelled" wins; the loser gets a clear answer. Tested in parallel on SQL Server. |
| Stock returned twice | The cancel is allowed only from Pending and is idempotent; a test cancels twice and in parallel and checks the stock once. |
| Polling drains battery or floods the server | Only while the screen is in front, 15 to 60 second intervals, backoff on failure, none for finished orders. |
| Someone cancels another customer's order | Owner-only, 404 for others, tested. |

## Tasks

| Task | What |
| --- | --- |
| 6.2 | API: store name and phone, estimate on the order (migration `AddOrderEstimate`) |
| 6.8 | Cancellation rules written down (D45) and API `cancel` with stock return and tests |
| 6.4 | Notifications: unread count and mark-all on the server; bell, list and screen in the app |
| 6.3 | App: live status polling for the order screen, the list and the bell |
| 6.6 | Reorder and Buy again tests (the feature is already built) |
| 6.7 | End-to-end check against the admin portal: staff change status, the app follows, for every allowed change and for cancel |
| 6.5 | Push notifications: **moved out of P6** (D44), kept on the dashboard as a later task |

## Proposed order of work

1. Your answers on D43 to D47, then sign-off.
2. Backend on a disposable database: migration, order fields, cancel, notification routes, tests including the race. Then backup, apply to the dev database, live checks.
3. App: polling, order screen, cancel, notifications, bell, tests.
4. Check on the phone, with you changing a status in the admin portal while the app is open.

## Rollback

The migration's Down drops the one column. The new routes and fields are additive, so the previous app build keeps working; cancelled orders stay as they are (a Cancelled status is already part of the server's list).
