# API contract notes (for clients)

- Machine-readable contract: [openapi.json](./openapi.json), re-exported **2026-10-09** after the P7 payment routes from a running Development API (`/swagger/v1/swagger.json`, OpenAPI 3.0, 59 paths). Re-export after any API change:

  ```powershell
  dotnet run --project src/QuickCommerce.Api --launch-profile http
  curl http://localhost:5067/swagger/v1/swagger.json -o docs/api/openapi.json
  ```

- Local base URL: `http://localhost:5067` (Android emulator: `http://10.0.2.2:5067`).

## Response envelope

```jsonc
// success
{ "success": true, "data": { } }
// failure
{ "success": false, "message": "Order not found", "errors": [ ] }
```

`errors` is a list of strings. Default ASP.NET model-validation failures may instead return `{ "errors": { "Field": ["message"] } }`; the mobile client accepts both.

| Status | Mobile exception |
| --- | --- |
| 400 / 422 | `ValidationException` |
| 401 | `UnauthorizedException` |
| 403 | `ForbiddenException` |
| 404 | `NotFoundException` |
| 409 | `ConflictException` |
| 429 | `TooManyRequestsException` |
| 5xx | `ServerException` |

## Headers

- `X-Correlation-ID`: send one per request; the API echoes it and logs it. The mobile client always sends one.
- `Authorization: Bearer <access token>` for protected routes. Login: `POST /api/auth/login` with `{ "username", "password" }`.
- JSON property names are camelCase; ids are GUID strings.

## Health

`GET /health/live` (process up) and `GET /health/ready` (database reachable) return plain text (`Healthy`) and status 200 / 503.

## What customers can call today (verified 2026-10-07)

| Route | Anonymous | Notes |
| --- | --- | --- |
| `GET /api/categories` | yes | |
| `GET /api/products/{id}` | yes | no MRP, Marathi name or availability yet |
| `GET /api/stores/nearest?latitude&longitude[&productIds]` | yes | |
| `GET /api/catalog/categories` | yes | **New (task 1.4).** Active categories with `translations`. |
| `GET /api/catalog/products?page&pageSize&search&categoryId` | yes | Task 1.1, extended by 1.3 and 1.4. Active products only, paged (default 20, max 50), search max 100 chars (also matches translated names), ordered by name. Returns `{ items, page, pageSize, totalCount, hasMore }`. Item fields: `id, name, description, price, mrp, discountPercent, unitOfMeasure, categoryId, imageUrl, translations`. Invalid paging returns 400. |
| `GET /api/catalog/products/{id}` | yes | **New (task 1.3/1.4).** Same item shape; 404 for unknown or inactive products. |
| `GET /api/catalog/stores?latitude&longitude` | yes | **New (task 1.13).** Stores that deliver to the point, nearest first: `[{ id, name, address, distanceKm, estimatedMinutes, serviceRadiusKm }]`. Empty list (200) when none does, 400 for out-of-range coordinates. A store that carries no active product is not listed. |
| `GET /api/catalog/stores/nearest?latitude&longitude` | yes | **New (task 1.10).** Nearest serviceable store: `{ id, name, distanceKm, estimatedMinutes, serviceRadiusKm }`. 404 when none serves the point, 400 for out-of-range coordinates. |
| `GET /api/products` | **no (401)** | admin-read policy; unchanged, for the admin portal |
| `GET /api/stores` | no | admin-read policy |
| `/api/customer/profile` | no | **New (task 2.5).** GET and PUT the signed-in customer's own profile `{ id, fullName, email, phoneNumber }`; email is read-only. |
| `/api/carts/*`, `/api/checkout/*`, `/api/orders`, `/api/customer/*` | no | customer token (`Orders` policy) |

`mobile/test/integration/api_smoke_test.dart` checks this against a running API (opt-in, see the file header).

## MRP, discount and translations (tasks 1.3, 1.4)

- `mrp` is always present: the stored MRP when it is above the price, otherwise the price. `discountPercent` is `round((mrp - price) / mrp * 100)` and 0 when there is no discount.
- `translations` is a map keyed by lowercase language code, for example `{ "mr": { "name": "...", "description": "..." } }`. Only languages that have a translation appear. A null or missing `description` means: use the English description. English stays in `name` and `description`.
- Categories use the same idea: `translations: { "mr": { "name": "..." } }`.
- Until the admin editor exists (plan task 1.12), values are set with `docs/sql/dev-seed-mrp-translations.sql` (development only).

## Store catalogue (task 1.13)

- `catalog/products?storeId=...&carriedOnly=true` returns only the products that store carries (it has an inventory row, whatever the quantity), with `totalCount` and paging for that set and the usual `inStock` / `lowStock` flags. `carriedOnly` without a `storeId` is a 400; an unknown or inactive store is 404. Without `carriedOnly` nothing changed: the whole catalogue, with stock flags when a `storeId` is given.
- `catalog/categories?storeId=...` returns only categories that have at least one product the store carries (a carried sub-category keeps its parent). Unknown store: 404.
- Store-filtered product pages are not cached (a primary-key lookup on the inventory table), so a catalogue change shows at once.
- The app sends `carriedOnly` whenever a store is selected and falls back to the whole catalogue when no store can be resolved.

## Stock and delivery estimate (tasks 1.2, 1.6, 1.10)

- `catalog/products` and `catalog/products/{id}` accept an optional `storeId`. With it, each product carries `inStock` and `lowStock` for that store (`lowStock` = in stock but at or below the reorder threshold). **Exact quantities are never returned.** Without `storeId` both fields are `null` (unknown). An unknown or inactive store returns 404; an empty GUID returns 400.
- Stock is added fresh on every request (the cached product page never contains it). It is display-only: checkout re-checks stock and remains the authority.
- `estimatedMinutes = BasePrepMinutes + ceil(distanceKm x MinutesPerKm)`, from the `Delivery` section of `appsettings.json` (`BasePrepMinutes: 8`, `MinutesPerKm: 2`). Invalid values stop the API at startup.
- Clients should round coordinates (the app uses 3 decimals, about 110 m) before calling `catalog/stores/nearest`.

## Customer accounts (P2, tasks 2.1 to 2.5 and 2.11)

All of these answer with the usual envelope. Passwords are 8 to 128 characters, must not equal the email and must not be a very common password.

| Route | Auth | Success | Failures |
| --- | --- | --- | --- |
| `POST /api/auth/register` `{ fullName, email, password, phoneNumber? }` | anonymous | 200, same body as login (tokens + user) | 400 validation (per-field messages in `errors`), 409 email already registered, 429 |
| `POST /api/auth/forgot-password` `{ email }` | anonymous | 200 `data: null`, **always** for a well-formed email so it cannot be used to find out who has an account | 400 malformed email, 429 |
| `POST /api/auth/reset-password` `{ email, code, newPassword }` | anonymous | 200; the code is single use and every session of the account is signed out | 400 "Invalid or expired code." (wrong, used, expired or too many attempts), 429 |
| `POST /api/auth/change-password` `{ currentPassword, newPassword, refreshToken? }` | customer token | 200; other sessions are signed out, the one named by `refreshToken` stays | 400 wrong current password or weak new one, 401, 429 |

- The reset code is 8 characters (shown as `XXXX-XXXX`), valid 30 minutes, 5 wrong attempts kill it, at most 3 codes per hour per account. Only a keyed hash is stored.
- 429 responses carry `Retry-After`. Defaults per client IP: login 10 per minute, register 5 per hour, forgot 5 per hour, reset 10 per hour, change 10 per hour (`RateLimits:Auth`). Counters are per API instance.
- 503 with a generic message means the feature is not configured (for example no reset-code key).

### Settings added

| Setting | Notes |
| --- | --- |
| `Account:ResetCodeKey` | **Required outside Development**, at least 32 characters. Supply through an environment variable or secret store, never source control. Development has a throwaway key in `appsettings.Development.json`. |
| `Account:CodeLifetimeMinutes`, `MaxCodeAttempts`, `MaxCodeRequestsPerHour` | Defaults 30, 5, 3. |
| `Registration:OrganizationId` | Optional. Without it new customers join the only active organization; with several active and none configured, registration answers 503. |
| `Email:Mode` | `Log` (Development only: writes mails to `%TEMP%quickcart-dev-outbox` and the log) or `Smtp`. Outside Development the API refuses to start without `Smtp`, `Host` and `From`. Credentials via environment variables. The provider is still an open decision (D23). |
| `Proxy:KnownProxies` | Addresses of the load balancers in front of IIS. Needed so rate limits see the real client IP; a warning is logged when empty. |
| `RateLimits:Auth:*` | See above. |

Applied to the dev database on 2026-10-07 as migration `20261007131144_AddCustomerAccountSupport` (adds `Users.PhoneNumber` and `PasswordResetCodes`), after a verified backup.

## Product variants (P3, tasks 3.2 to 3.4)

Every product has one or more **variants** (pack sizes such as 250 g, 500 g, 1 kg), each with its own price, MRP and stock per store. Every product has exactly one **default** variant. Everything below is additive: clients written before variants keep working unchanged.

- **Catalogue** (`catalog/products`, `catalog/products/{id}`): each product gains `variants: [{ id, label, price, mrp, discountPercent, isDefault, inStock, lowStock }]`, active variants only, ordered by sort order. The product's own `price`, `mrp`, `discountPercent`, `unitOfMeasure`, `inStock` and `lowStock` describe its **default variant**. `inStock` / `lowStock` (product and variant) are `null` without a `storeId` and are never quantities. A product "carried" by a store (`carriedOnly`) is one for which the store has a stock row on at least one active variant.
- **Cart:** `POST api/carts/{storeId}/items` takes an optional `variantId` next to `productId`; omitted means the default variant. A cart line is one per variant (the same product in two pack sizes is two lines). `PUT` and `DELETE api/carts/{storeId}/items/{productId}` take an optional `?variantId=`; omitted, they act on the product's only line, or its default variant's line when there are several. Cart items now also return `variantId` and `variantLabel`. A variant that does not belong to the product, or is inactive, is 404; an empty GUID is 400.
- **Checkout and orders:** the variant's price is re-checked against the cart's price snapshot (409 on a change) and its stock is taken in a transaction with the existing concurrency check. A concurrent purchase of the same stock row is retried a few times before a stock conflict is reported, so a customer is not turned away while stock remains. Order lines (`OrderItemResponse`) carry `variantId` and `variantLabel`; the order keeps the pack label, price and MRP as they were at purchase, so later price edits never change it. Orders placed before variants existed have these as `null`.
- **Admin order creation** (`OrderLineRequest`): optional `variantId`, default variant otherwise; the store chosen must stock every named variant.
- **Not changed:** `GET api/products` (admin) still shows the product's own price columns; variant management in the admin portal comes with product editing (task 1.12).

Database: migration `AddProductVariants` adds `ProductVariants` and `StoreVariantInventory`, backfills a default variant per product and a stock row per existing stock row, and keeps the old `StoreInventory` table untouched as the rollback copy. See `docs/plans/P3-product-variants.md`, `docs/sql/dev-seed-variants.sql` (dev packs) and `docs/sql/p3-reverse-sync-inventory.sql` (code-only rollback).

## Addresses and serviceability (P4, tasks 4.1, 4.2, 4.8, 4.9)

**Saved addresses** (`api/customer/addresses`, customer token, `Orders` policy). The customer comes from the token, never from the request. An address that belongs to someone else is **404, exactly like one that does not exist**, so its existence is not revealed.

| Route | Does |
| --- | --- |
| `GET` | The caller's addresses, default first, then most recently changed. Each carries `serviceable` and `serviceabilityReason`, computed from today's stores. |
| `POST` | Creates one (201). The first address becomes the default. Over the limit: 409. |
| `PUT {id}` | Updates the fields; the id and the default flag are kept. |
| `POST {id}/default` | Makes it the default and clears the previous default in one transaction. |
| `DELETE {id}` | Deletes it. Deleting the default promotes the most recently changed remaining address. |

Body of `POST` and `PUT`: `{ label, line, flatOrBuilding?, landmark?, latitude, longitude, receiverName, receiverPhone }`. `label` 1 to 40 characters, `line` up to 300, flat and landmark up to 120, receiver name up to 120, phone 10 to 15 digits (stored normalized, as for accounts), coordinates in range and not 0,0. Validation failures are 400 with a message per problem. The limit is `Addresses:MaxPerCustomer` (default 10, 1 to 50). Changes to one customer's addresses run one after another (a short database lock), so parallel requests cannot exceed the limit or leave two defaults; the table also enforces one default per customer.

Receiver name and phone are personal data: returned only to the owner, never logged, and not visible to staff through this API.

**Serviceability** (`GET api/catalog/serviceability?latitude&longitude`, anonymous, for guests and the picker): `{ serviceable, reason, nearestDistanceKm }`. `reason` is `Serviceable`, `OutsideServiceArea` (no active store's delivery radius covers the point) or `NoStoreAvailable` (a radius covers it but no covering store carries any active product). `nearestDistanceKm` is the distance to the nearest store that serves the point, or to the nearest store at all when none does; null when there are no stores. Out-of-range coordinates are 400. The same rule drives the `serviceable` flag on saved addresses and the checkout check.

**Checkout** (`POST api/checkout/{storeId}`): the store must actually deliver to the delivery coordinates, checked on the server before anything in the cart or stock changes. Otherwise **409** with the usual `message` plus `"reason": "OutsideServiceArea"`. Radius is inclusive: a point exactly at the radius is served.

Database: migration `AddCustomerAddresses` adds the `CustomerAddresses` table only (cascade from the customer, one default per customer, coordinate checks). Its Down drops that table. See `docs/plans/P4-addresses-and-serviceability.md`.

**Secrets:** the Google Maps and Places API key is never committed. It lives in the untracked `mobile/android/secrets.properties` and `mobile/config/local.json`, both git-ignored, and a test (`ApiKeyGuardTests`) fails the build if a Google key appears anywhere in the repository.

## Cart, fees and checkout (P5, tasks 5.3, 5.5, 5.8, 5.9)

Status: built and tested; **not yet applied to the dev database** (needs a backup and your go-ahead). The request and response fields below are additive, so an older app keeps working.

**Fees** come from the `Pricing` configuration section (`DeliveryFee`, `HandlingFee`, `FreeDeliveryThreshold`), one set for the organisation. The API refuses to start without the section, and a negative or absurd value also stops start-up. A subtotal at or above the threshold ships free (a threshold of 0 means delivery is never free). An empty cart has no fees. The app never holds its own copy.

**Fee settings for guests:** `GET api/catalog/pricing` (anonymous) returns `{ deliveryFee, handlingFee, freeDeliveryThreshold }`, so a cart built before signing in shows the same fees the server will charge.

**Cart responses** (`CartResponse`) now also carry `subtotal`, `deliveryFee`, `handlingFee`, `total` (what the customer pays), `freeDeliveryThreshold` and `amountToFreeDelivery`. `totalAmount` is unchanged: the sum of the lines. Each line also carries `available` (what the store can supply now), `unavailable` (the product or pack is gone, so checkout would refuse) and `currentUnitPrice` (the price now, next to `unitPriceSnapshot`, the price when it was added; checkout refuses when they differ). Cart answers are `{ success, data }` with `data` the cart; the merge route is the exception (see below).

| Route | What |
| --- | --- |
| `GET api/carts/current` | The customer's cart in whichever store it is; **204** when there is none. |
| `DELETE api/carts/{storeId}` | Empties the cart (deletes it and its lines). Succeeds when there was none. |
| `POST api/carts/{storeId}/merge` | Body `{ items: [{ productId, variantId?, quantity }] }`, 1 to 100 lines. Answer `data` is `{ cart, notes }`. Adds a guest's device cart: the same pack asked for twice is one line, quantities are added to what is already there, and each line is capped at what the store has. Existing lines keep their snapshot price. `notes` lists what did not go as asked: `Unavailable` (product or pack gone or unknown), `OutOfStock` (none left), `Reduced` (added fewer; `quantity` is how many were added). A bad line rejects the whole request (400) before anything is added. |
| `POST api/carts/{storeId}/reprice` | Brings every line to the current price after the customer has accepted the change. A line that is gone is left, marked `unavailable`, for the customer to remove. 404 without a cart. |

**Checkout** (`POST api/checkout/{storeId}`):

- Body is either `{ addressId }` (the customer's own saved address; another customer's or a missing one is **404**; the server copies its text, point, receiver name and phone onto the order, so later edits do not change the order) or, as before, `{ deliveryAddress, latitude, longitude }`.
- Header **`Idempotency-Key`** (8 to 64 letters, digits, `-` or `_`; anything else is 400): send one new value per attempt to order, and the **same** value when retrying or when the customer taps twice. The first request is **201**; a repeat returns the same order with **200** and `"replayed": true`. A second order is never created and stock is taken once, including when the taps arrive at the same moment. The same key for a different store or address is **409** `IdempotencyKeyReused`. Keys are remembered for 24 hours. Without the header there is no protection (older clients).
- The order carries `subtotalAmount`, `deliveryFee`, `handlingFee`, `totalAmount` (the amount to pay), `paymentMethod` (`CashOnDelivery`), and `receiverName` / `receiverPhone` (null when a typed address was used). Orders placed before fees existed read back with subtotal equal to total and no fees.
- Every failure is **409** with `message`, a stable `reason`, and `details`, the cart lines involved (empty when not applicable):

| `reason` | Meaning | `details` (per line) |
| --- | --- | --- |
| `OutsideServiceArea` | The store does not deliver to the address. | none |
| `CartEmpty` | Nothing to order, for example already ordered or emptied on another phone. | none |
| `ProductUnavailable` | The product or pack is gone. Takes priority over the others. | `productId, variantId, name, label, quantity` |
| `InventoryConflict` | The store has fewer than the cart asks for. | as above plus `available` |
| `PriceChanged` | The shop's price differs from the cart's. | as above plus `oldPrice`, `newPrice` |
| `IdempotencyKeyReused` | See above. | none |

All lines are checked before anything changes, so one answer lists every problem, and when any line fails no stock is taken and the cart is left as it was. After `PriceChanged` the customer is shown the change; `reprice` then updates the cart.

## Order tracking, cancelling and notifications (P6, tasks 6.2, 6.4, 6.8)

Status: built, tested and applied to the dev database (2026-10-09). Everything below is additive, so an older app keeps working.

**Order answers** (`OrderResponse`, for `api/customer/orders`, the checkout answer and a cancel) gain `storeName`, `storePhone` (null until staff fill it in) and `estimatedDeliveryMinutes`. The estimate is the one the customer was given when the order was placed (distance from the store to the delivery point, with the delivery settings); it stays on the order if the store's estimate changes later. Orders placed before this have no estimate (null). The admin order answers do not carry these.

**Cancelling:** `POST api/customer/orders/{orderId}/cancel` (customer token, no body).

| Answer | When |
| --- | --- |
| **200** `{ success, data: <order> }` with `status` Cancelled | The order was waiting for the shop (Pending), or it was already cancelled (the same answer as the first time, nothing changes again). |
| **409** `{ success: false, message, reason: "OrderNotCancellable" }` | The shop has already accepted or finished with it (Accepted, Preparing, Ready, Completed, Rejected). The customer is told to contact the store. |
| **404** | Not the caller's order, or no such order. |

A successful cancel, in one transaction: the status becomes Cancelled (with a status-history row), the stock of every line goes back to the store, and the customer gets a notification ("Order cancelled" / "Your order was cancelled."). If the shop accepts at the same moment, exactly one of the two wins; staff can no longer accept a cancelled order (the existing transition rule refuses it). No refund is involved while payment is cash on delivery. When the shop rejects an order instead, its stock goes back to the store the same way (once).

**Notifications:**

| Route | What |
| --- | --- |
| `GET api/customer/notifications/unread-count` | `{ success, data: { count } }`. Cheap enough to ask every minute for the bell. |
| `PUT api/customer/notifications/read-all` | Marks all of the caller's unread notifications as read: `{ success, data: { updated } }` (0 when there were none). |

The existing list (`GET api/customer/notifications`, `unreadOnly`) and marking one read are unchanged.

**Store phone:** `Stores.PhoneNumber` (up to 20 characters, optional). The dev stores get numbers from `docs/sql/dev-seed-store-phones.sql`; staff will be able to set it when store editing exists.

## Online payment (P7, tasks 7.2 to 7.5, 7.7)

Status: built and tested (2026-10-09); migration `AddOnlinePayments` applied to the dev database (backup `retail-mobile-app_pre-P7-payments.bak`). The feature is **off** until `Payments:Enabled` is true, so an older app and cash on delivery are unaffected. Provider: Razorpay (hosted checkout; card and UPI details never reach this server).

**Settings** (section `Payments`; the two secrets only in environment variables or user secrets, never in a committed file): `Enabled` (default false), `KeyId`, `KeySecret`, `WebhookSecret`, `HoldMinutes` (default 15, 5 to 60), `ReconcileAfterMinutes` (20), `JobIntervalSeconds` (60). When `Enabled` is true the server refuses to start if a key is missing.

**Choosing online payment:** `POST api/customer/checkout/...` takes an optional `paymentMethod` (`CashOnDelivery`, the default, or `Online`). With `Online` the order is created with status **AwaitingPayment**, its items are held, and `paymentExpiresAt` is set (now + hold). While payments are off the answer is **409** `reason: "PaymentsUnavailable"` and nothing is taken. The idempotency hash includes the method, so one key cannot be reused for the other method (409 `IdempotencyKeyReused`). Order answers gain `paymentStatus` (a number: 0 NotRequired, 1 Created, 2 Failed, 3 Paid, 4 Refunding, 5 Refunded, 6 RefundFailed) and `paymentExpiresAt`; the order status list gains **AwaitingPayment** (last value).

| Route | What |
| --- | --- |
| `GET api/payments/options` (anonymous) | `{ cashOnDelivery, online, holdMinutes }`: whether to offer the online choice and how long items are held. |
| `POST api/customer/orders/{orderId}/payment` | Starts or resumes paying: returns `provider`, `keyId` (public), `providerOrderId`, `amountPaise` (the server's own total), `currency`, `orderNumber`, `expiresAt`. Calling it again reuses the same provider order, so one order can never be charged twice. |
| `POST api/customer/orders/{orderId}/payment/confirm` | Body `{ providerOrderId, providerPaymentId, signature }` from the checkout screen. The server checks the signature, asks the provider for the payment and requires it captured for the right order and amount, then marks the order paid (status Pending, visible to the shop). Repeating it changes nothing. |
| `POST api/payments/razorpay/webhook` (anonymous) | The provider's signed notifications (`payment.captured`, `order.paid`, `payment.failed`, `refund.processed`, `refund.failed`). The raw body is verified with the webhook secret; duplicates are ignored by event id; **400** for a bad signature, **500** to make the provider retry. |

Reasons on a **409** from start or confirm: `PaymentsUnavailable`, `NotAnOnlineOrder`, `PaymentHoldExpired`, `AlreadyPaid`, `OrderNotAwaitingPayment`, `PaymentSignatureInvalid`, `PaymentMismatch`, `PaymentAmountMismatch`, `PaymentNotCaptured`, `PaymentProviderUnavailable`. Another customer's order is **404**.

**Rules:** the shop sees an online order only after it is paid. A payment that arrives after the hold ran out, or for a different amount, is refunded automatically and the order stays cancelled. The customer cancelling a paid order (while still Pending) or the shop rejecting it queues a full refund to the original method; an unpaid order cancelled just releases the items. A background job (every `JobIntervalSeconds`) releases the items of unpaid orders after the hold, starts queued refunds (retrying temporary provider errors, marking permanent refusals `RefundFailed`), and looks up payments whose notification never arrived. Every state change is one transaction and safe to run twice or on several servers.

## Order numbers (P10)

Status: built and tested (2026-10-09); migration `AddStoreCodesAndOrderNumberCounters` applied to the dev database (backup `retail-mobile-app_pre-P10-order-numbers.bak`).

New orders are numbered `STORE-yyMMdd-nnnn`, for example `KHG-261009-0042`: the store's code (`Stores.Code`, up to 8 letters or digits, unique), the day in India time, and that store's count for the day (restarts at 0001 each day, grows past four digits if needed). The number is given inside the order's own transaction, so two orders never share one and a refused order does not use up a number. Orders placed before keep their old numbers. `orderNumber` in every answer is simply this string. Existing stores get a code from the first three letters of their name when the migration runs; `docs/sql/dev-store-codes.sql` sets the preferred dev codes (KHG, PNV, HBR, BLP, CDR, NSF).

## Notification system (P11)

Status: built and tested (2026-10-09); migration `AddNotificationSystem` applied to the dev database (backup `retail-mobile-app_pre-P11-notifications.bak`).

**Types.** Every notification has a `type` and a `category` (`Order`, `Payment`, `Offer`, `System`). Types: OrderPlaced, OrderAccepted, OrderPacking, OutForDelivery, OrderDelivered, OrderRejected, OrderCancelled, PaymentReceived, PaymentNotCompleted, PaymentProblem, PaymentWillBeRefunded, RefundProcessed, Offer. The words for each (English and Marathi) live in one place (`NotificationCatalog`); an order event only names the type and the order number. Older rows with other types are shown exactly as stored.

**Reading.** `GET api/customer/notifications?unreadOnly=&lang=mr` returns `title` and `message` written in the requested language (`mr` for Marathi, anything else English) plus `category`. Everything else on that route is unchanged.

**Order events** now notify the customer: placed (cash orders, at checkout; online orders get "Payment received" once paid), accepted, packing (Preparing), out for delivery (Ready), delivered (Completed), declined, cancelled; and the payment ones (received, not completed, problem, will be refunded, refund processed). Each message names the order number.

**Preferences.** `GET` and `PUT api/customer/notifications/preferences` with `{ "offers": true|false }`. Only offers can be switched off; order and payment messages are always sent.

**Offers (admin).** `GET api/admin/campaigns`, `POST api/admin/campaigns` (`titleEn`, `bodyEn` required up to 160 and 500 characters; `titleMr` and `bodyMr` both or neither; `startsAt` optional, a past or empty time means now), `POST api/admin/campaigns/{id}/cancel` (409 `CampaignAlreadySent` once sent). Admin only. A background job (every 30 seconds, safe on several servers) sends each due campaign once to every active customer with offers on; the count is kept on the campaign. Status is `Scheduled`, `Sent` or `Cancelled`. Push (FCM) will reuse the same types later.
