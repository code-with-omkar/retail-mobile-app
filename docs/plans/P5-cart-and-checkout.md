# Mini-plan: cart and checkout with cash on delivery (P5)

Status: **approved 2026-10-09** (all decisions D35 to D42 accepted as recommended). **Backend and app built and tested 2026-10-09.** Backend applied to the dev database after a verified backup (backup retail-mobile-app_pre-P5-checkout; 16 migrations; 35 live checks passed). App: 317 tests, plus a live cart-and-checkout test against the dev API (account, order and stock cleaned up afterwards). One backend addition after approval: the public `GET api/catalog/pricing` so a guest sees the same fees. Waiting for a check on the phone.

## Why

Today the cart and the order are pretend. The cart lives only in the phone's memory and is lost when the app closes. "Place order" adds a made-up order to a local list (`QC1025`, `QC1026`, ...) and never calls the server, so the shop never receives it. The fees (delivery 25, handling 5, free delivery over 199) are constants inside the app. The server already has a cart and a checkout, written for the earlier prototype, but the app does not use them and they have gaps: no protection against a double tap creating two orders, no fees, and the failure reasons are not specific enough for the app to explain.

P5 makes the order real: a cart that lives on the server for signed-in customers, prices and fees decided by the server, a checkout that cannot create duplicates, and a clear explanation and a way to fix each thing that can go wrong. Payment is **cash on delivery** only; online payment is P7.

## Decisions

I have put my recommendation first in each row. Please confirm or change them.

| # | Decision | Recommendation |
| --- | --- | --- |
| D35 | **Where the cart lives.** | Signed-in customers: on the server, so it survives closing the app and changing phone. Guests: on the device only, merged into the server cart when they sign in. |
| D36 | **Merging a guest cart on sign-in.** | Quantities are added together for the same pack, then capped at what the store can supply. Anything dropped is listed to the customer ("2 items were not available"). If the server cart belongs to another store, the customer chooses which cart to keep (same rule as switching store, D26). |
| D37 | **Fees.** | Delivery fee, handling fee and the free-delivery threshold are server configuration (one set for the whole organisation for now), returned with every cart. The app no longer contains them. Per-store fees are out of scope. |
| D38 | **Duplicate protection.** | The app sends an `Idempotency-Key` header with each checkout attempt. The same key returns the same order; the same key with a different store or address is refused. Keys are kept for 24 hours. |
| D39 | **Payment in P5.** | Cash on delivery only. The order records its payment method (`CashOnDelivery`) so P7 can add others without changing orders. The app's UPI and card options are hidden until P7 (they do nothing today). |
| D40 | **A price changes while the customer is checking out.** | The order is **not** placed. The app shows which lines changed and the new total, and the customer confirms before trying again. Prices are never accepted silently. |
| D41 | **Stock.** | No reservation in P5. Stock is checked and taken at the moment the order is placed, as the server does now. A customer can lose the last item to someone faster; they are told which line and can adjust. Holding stock during payment is task 7.5. |
| D42 | **Orders list and detail.** | Bring task 6.1 forward into P5: after this change the Orders tab and Track order read the **real** orders. Otherwise a customer would place a real order and not see it. The status timeline polling stays in P6. |

## Today

- Server cart: `GET carts/{storeId}`, add, change and remove a line. No "which cart do I have", no clear, no merge. Totals are the sum of lines only.
- Server checkout: `POST checkout/{storeId}` with address text and coordinates. It checks the delivery radius (P4) and turns the cart into an order, taking stock per variant with a bounded retry. Failures come back as `409` with a message, and only the radius failure carries a machine-readable `reason`.
- Server order: `TotalAmount`, address text, coordinates, status history, line snapshots (name, price, MRP, pack). No fees, no receiver name or phone, no payment method, no idempotency.
- App: cart is a local map of pack to quantity; totals and fees are computed in the app from constants; checkout writes to a local list; order success shows a made-up number.

## Design

### Database (additive)

One new table and a few nullable or defaulted columns. Nothing is renamed or dropped; existing orders keep reading exactly as before.

- `CheckoutRequests`: `Id`, `CustomerId`, `IdempotencyKey` (max 64), `RequestHash` (of store, cart contents and address), `OrderId`, `CreatedAt`. Unique on (`CustomerId`, `IdempotencyKey`). The order and this row are written in the same transaction, so a retry after a crash finds either both or neither.
- `Orders`: add `SubtotalAmount`, `DeliveryFee`, `HandlingFee` (default 0; existing orders get `SubtotalAmount = TotalAmount` and zero fees in the migration), `PaymentMethod` (default `CashOnDelivery`), `ReceiverName`, `ReceiverPhone` and `DeliveryAddressId` (all nullable). `TotalAmount` stays the amount to pay, so the admin portal and old orders are unaffected.

Migration rules as before: schema only, Up and Down reviewed, applied first to a disposable database, then to the dev database after a verified backup. Rollback drops the table and the new columns.

### API

All routes use the `Orders` policy; the customer comes from the token.

- `GET api/carts/current`: the customer's cart (or `204`), so the app can restore it.
- `DELETE api/carts/{storeId}`: empty the cart (used by "change store" and after an order).
- `POST api/carts/{storeId}/merge`: add a list of `{productId, variantId, quantity}` in one transaction; returns the cart and a list of lines that were skipped or reduced, each with a reason. Used for the guest merge.
- `CartResponse` gains `subtotal`, `deliveryFee`, `handlingFee`, `total`, `freeDeliveryThreshold`, `amountToFreeDelivery`, and per line `available` (what the store can supply now) and `unavailable` (the product or pack is gone). `totalAmount` is kept for older clients.
- Fees come from a `Pricing` configuration section; checkout calculates them again at the moment of ordering, never from the app.
- `POST api/checkout/{storeId}` takes an optional `addressId` (the customer's saved address; the server copies its text, receiver and point onto the order, so later edits do not change the order) or, for compatibility, the existing address text and coordinates. It requires the `Idempotency-Key` header from this version on; a missing header is accepted for old clients and simply gets no duplicate protection.
- Failures all return `409` with a `reason` and a `details` list the app can act on:
  - `OutsideServiceArea` (exists)
  - `CartEmpty`
  - `ProductUnavailable` with the lines that are gone
  - `PriceChanged` with each line's old and new price and the new total
  - `InventoryConflict` with each line's available quantity
  - `IdempotencyKeyReused` (same key, different request)
- Success returns `201` with the order including the fee breakdown; a repeat with the same key returns `200` with the same order.

### Mobile

- **Cart:** a `CartRepository` with two implementations, a device-only one for guests and the server one for signed-in customers, behind the cart provider the screens already use. Changes show at once and are sent in the background; if the server refuses, the line goes back to its old quantity and the customer is told why (optimistic update with rollback).
- **Sign-in:** the guest cart is merged (D36), then the server cart is shown. Signing out keeps the server cart on the server and starts an empty local one.
- **Totals:** the cart and checkout show the server's subtotal, fees and total. The "add Rs X more for free delivery" bar uses the server's threshold. The constants are deleted from the app (the offline seed mode keeps its own copy).
- **Checkout:** places the order with the selected saved address and a fresh idempotency key per attempt (the same key is reused if the customer taps again or the network drops, which is what makes retries safe). One tap disables the button until the answer arrives.
- **Each failure has its own screen state**, in English and Marathi:
  - price changed: the changed lines with old and new price, new total, "Update and review" (D40)
  - item unavailable: the lines are marked, "Remove unavailable items" and continue
  - not enough stock: shows how many are left, "Use available quantity"
  - cart empty (for example emptied on another phone): returns to the cart
  - outside the service area: the P4 message, "Choose another address"
  - network error or timeout: "We could not confirm your order. Check your orders before trying again." and, if the order exists, it appears in Orders because the same key returns it
- **Success:** shows the real order number, total and the store's delivery estimate, clears the cart, and "Track order" opens the real order. The Orders tab and the order detail read `api/customer/orders` (D42), with the fee breakdown.

## Out of scope

- Online payment, UPI, cards, wallets (P7); reserving stock while paying (7.5).
- Cancellation and refunds (6.8, 7.7); live status updates, notifications and push (P6).
- Coupons, minimum order value, delivery slots, tips, per-store fees.
- Admin portal changes (it keeps reading `TotalAmount`).

## Impact on consumers

| Consumer | Impact |
| --- | --- |
| Customer app (current build) | Keeps working: new response fields are extra, the idempotency header is optional, `totalAmount` is unchanged. It still keeps its cart locally until the new build is installed. |
| Admin portal | None. Orders show the same total; fee breakdown and receiver details can be shown later. |
| Existing orders | Unchanged. New columns have defaults; the migration sets the subtotal for old rows. |

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Duplicate orders from double taps or retries | Idempotency key stored with the order in one transaction; unique index; parallel-submit test on SQL Server. |
| Two phones changing the same cart | Server is the source of truth; every change returns the whole cart; the app refreshes on resume. |
| Stock lost between cart and order | Checked and taken atomically at order time (existing); specific message and fix path. |
| Customer cannot tell if an order was placed after a network failure | Same-key retry returns the existing order; the message tells the customer to check Orders; Orders reads the server. |
| Guest merge surprises the customer | Result list shown; a cart for another store asks first. |
| Fees configured wrongly | Validated at start-up (non-negative, threshold above zero); totals computed in one place and covered by tests. |
| Personal data on orders (receiver phone) | Copied to the order only; not logged; returned only to the owner and to staff through the order. |

## Tasks

The dashboard tasks 5.1 to 5.7 stay as they are. Three are added, and 6.1 moves here.

| Task | What |
| --- | --- |
| 5.8 (new) | Migration: `CheckoutRequests` table and the order fee, receiver and payment-method columns |
| 5.9 (new) | API: `carts/current`, clear, merge; checkout accepts `addressId`; machine-readable failure reasons and details |
| 5.3 | Server pricing breakdown from configuration |
| 5.5 | Idempotency key on checkout |
| 5.1 | App cart repository (guest and server) with rollback |
| 5.2 | Guest cart merge on sign-in |
| 5.4 | App checkout and each failure state |
| 5.6 | Order success from the real order; clear cart |
| 6.1 (moved from P6) | Orders list and detail from the server |
| 5.10 (new) | Hide UPI and card options until P7 |
| 5.7 | Tests: the conflict paths on the API (SQL Server, disposable database) and the app flows |

## Proposed order of work

1. Your answers on D35 to D42, then sign-off on this plan.
2. Backend on a disposable database: migration, pricing, cart routes, idempotency, failure details, tests (5.8, 5.9, 5.3, 5.5, 5.7). Then backup, apply to the dev database, live checks including a double submit and a price change.
3. App: cart repository and merge, totals, checkout with failure states, success, real orders (5.1, 5.2, 5.4, 5.6, 6.1, 5.10) with widget tests.
4. Check on the phone: add items, close and reopen the app, sign in from a guest cart, place an order, tap twice, change a price in the database and retry, run out of stock.

## Rollback

The migration's Down removes the new table and columns; orders keep their totals. Server changes are additive, so the previous app build keeps working. The previous app build can be reinstalled at any time; its local cart and fake orders return with it.

## Cleanup after verification

Test customers, carts and orders created during live checks are deleted from the dev database, as in earlier phases.
