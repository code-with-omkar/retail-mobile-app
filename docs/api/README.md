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

## Store-owned offers (P13)

Status: built and tested (2026-10-12); migration `AddCampaignStores` applied to the dev database (backup `retail-mobile-app_pre-P13-campaign-stores.bak`).

Offers belong to stores (`CampaignStores`; none means every store). Routes are the same, `api/admin/campaigns` (list, create, `{id}/cancel`), now open to administrators and store staff (policy `OfferManagement`):

| Who | Create | List | Cancel |
| --- | --- | --- | --- |
| Administrator | for any chosen stores, or none = every store (`storeIds`) | every offer | any |
| Store staff | always for their own store (empty `storeIds` means their store; another store is **403**) | offers that are for their store | only offers for their store alone (others look like **404**) |

The request gains optional `storeIds`; the answer gains `stores` (id and name) and `allStores`. **Audience:** an offer for chosen stores goes to customers of those stores who have offers on, where a customer of a store has ordered from it or has a cart there; an offer for no store goes to every customer with offers on. Each notification carries the store name; `GET api/customer/notifications` returns it as `storeName` (null for an offer from the whole business), and the app shows "From {store}".

## Offer publishing fee (P14)

Status: built and tested (2026-10-12); migration `AddOfferFees` applied to the dev database (backup `retail-mobile-app_pre-P14-offer-fees.bak`).

**Who pays what.** Store staff pay a fee to publish an offer: their store's own fee if an administrator set one, otherwise the default; 0 is free. Offers an administrator creates are always free. The fee is fixed on the offer when it is created (`feePaise`), so later changes do not touch it. While the fee is unpaid the offer's status is `AwaitingPayment` and it is **never sent**; once paid it is `Scheduled` and goes out at its start time. Creating an offer with a fee while online payment is off is **409** `PaymentsUnavailable` (a free offer still works).

| Route | What |
| --- | --- |
| `GET api/admin/offer-fees` (admin) | `{ defaultPaise, stores: [{ storeId, name, overridePaise, effectivePaise }] }` |
| `PUT api/admin/offer-fees/default` (admin) | `{ amountPaise }`, 0 to 1,00,00,000 (₹1,00,000) |
| `PUT api/admin/offer-fees/stores/{storeId}` (admin) | `{ amountPaise }`, or `null` to remove the store's own fee |
| `GET api/admin/offer-fees/mine` | What an offer would cost the person asking (staff: their store's fee; admin: 0) and whether online payment is on |
| `POST api/admin/campaigns/{id}/payment` | Starts or resumes paying: `provider`, `keyId`, `providerOrderId`, `amountPaise`, `currency`. Asking again reuses the same provider order, so the fee is never charged twice |
| `POST api/admin/campaigns/{id}/payment/confirm` | `{ providerOrderId, providerPaymentId, signature }` from the checkout. The server checks the signature and asks the provider itself; repeating it changes nothing |

Reasons on a **409**: `PaymentsUnavailable`, `OfferNotAwaitingPayment`, `AlreadyPaid`, `PaymentSignatureInvalid`, `PaymentMismatch`, `PaymentAmountMismatch`, `PaymentNotCaptured`, `PaymentProviderUnavailable`. Another store's offer is **404**.

**Cancel and refund.** Cancelling an offer before it is sent refunds a paid fee in full to the original payment method (a background step, retried when the provider is busy; a refusal marks it `RefundFailed`); an unpaid one is just closed. A payment that arrives after the offer was cancelled, or for the wrong amount, is refunded automatically. A fee whose confirmation never arrived is found by a background lookup. Payment status uses the same numbers as order payments (0 NotRequired, 1 Created, 2 Failed, 3 Paid, 4 Refunding, 5 Refunded, 6 RefundFailed).

## Paid placement (P15)

Status: built and tested (2026-10-12); migration `AddPaidPlacement` **not yet applied** to the dev database.

A store bids for first place in a pincode for a week (week starts Monday 00:00 India time; bidding for a week closes when it starts) and pays up front. The highest **paid** bid leads; a higher paid bid replaces it and the beaten bid is refunded in full. An equal bid loses to the earlier payer. A payment that arrives late, for the wrong amount, or for a cancelled bid is refunded. Unpaid bids never lead.

| Route | What |
| --- | --- |
| `GET api/placement/rules` | `enabled, minBidPaise, minStepPaise, maxWeeksAhead` (defaults on, ₹500, ₹100, 4) |
| `PUT api/placement/rules` (admin) | Same fields |
| `GET api/placement/market?pincode=` | Weeks ahead with the highest bid, whether it is yours, and the least to bid |
| `GET api/placement/bids` / `POST api/placement/bids` | List; place `{ pincode, weekStart, amountPaise, storeId? }` (admin picks the store; max 20 unpaid bids per store) |
| `POST api/placement/bids/{id}/cancel` | Cancel (a paid bid is refunded) |
| `POST api/placement/bids/{id}/payment` and `/payment/confirm` | Same shape as offer fees |

**Customers:** `GET api/catalog/stores?latitude&longitude&pincode` lists the pincode's leading store first with `sponsored: true`, only if that store already delivers there. The app shows a "Sponsored" tag.

## Delivery partners (P12)

Status: built and tested (2026-10-12); migration `AddDeliveryAssignment` applied to the dev database (backup `retail-mobile-app_pre-P12-delivery.bak`). Orders gain `DeliveryPartnerId`, a four-digit code, cash collected and pickup/delivery times.

**Flow.** Staff give a **Ready** order to an active delivery partner of the same store (a partner is a user with role `DeliveryPartner` and a store). The partner picks it up (status **OutForDelivery**, customer notified), then completes it (**Delivered**, customer notified) with the code the customer sees in the app and the cash collected. Once a partner holds an order, staff can no longer set it Completed.

| Route | Who | What |
| --- | --- | --- |
| `GET api/delivery/partners?storeId=` | staff (own store), admin (names the store) | Active partners: `id, name, phone, activeDeliveries` |
| `POST api/operations/orders/{id}/assign` | staff, admin | `{ partnerId }`. **404** other store's order, **400** not an active partner of the store, **409** order not Ready. Same partner again changes nothing; changing partner while Ready keeps the code |
| `GET api/delivery/mine` | partner | Ready and out-for-delivery orders, plus those delivered in the last 24 hours: address, receiver name/phone, items, `cashToCollect` |
| `POST api/delivery/{id}/pickup` | partner | Ready to OutForDelivery. Repeating changes nothing |
| `POST api/delivery/{id}/deliver` | partner | `{ otp, cashCollected }`. Cash on delivery: exactly the total; online: 0 (**400** otherwise). Wrong code **400** `reason: WrongCode`; 5 wrong codes lock the order 15 minutes (**429** `reason: Locked`) |

**Customer.** `GET api/customer/orders` and `/{id}` add `deliveryPartnerName` (first name), `deliveryPartnerPhone` and `deliveryOtp` **only while the order is out for delivery**. Staff and the portal never get the code. Another partner's order looks like **404**.

**Portal.** Orders list shows the partner (`deliveryPartnerId`, `deliveryPartner`); Ready orders get a partner picker with Give / Change.

## Catalogue editing, pictures and stock (P16: tasks 1.5, 1.12, 3.5)

Status: built and tested (2026-10-12). **No database migration**: it uses existing tables.

**Who.** Administrators (`Admin`, `ApplicationAdmin`) edit the shared catalogue; **store staff set their own store's stock**; an administrator names the store. Everyone else is **403**.

| Route | What |
| --- | --- |
| `GET api/admin/catalog/products?search=&page=&pageSize=` | Products (also switched-off ones) with sizes and Marathi text |
| `POST api/admin/catalog/products` | `{ sku, name, description, categoryId, unitOfMeasure, price, mrp? }`; also makes the first (default) size. **409** SKU used |
| `PUT api/admin/catalog/products/{id}` | `{ name, description, categoryId, unitOfMeasure, isActive }` (SKU never changes) |
| `PUT / DELETE api/admin/catalog/products/{id}/translations/mr` | `{ name, description? }`; only `mr` is accepted |
| `POST api/admin/catalog/products/{id}/variants` | `{ sku, label, price, mrp?, sortOrder, isActive }`; new sizes are never the default |
| `PUT .../variants/{variantId}` | Same body. The default size cannot be switched off (**400**) |
| `POST .../variants/{variantId}/default` | Makes an active size the default, in one step |
| `POST api/admin/catalog/products/{id}/image` | Form field `file`: JPEG, PNG or WebP, up to 2 MB, judged by content not name (**400** otherwise). Returns the product with `imageUrl` = `/media/products/...` |
| `DELETE api/admin/catalog/products/{id}/image` | Removes the picture and its file |
| `GET api/stock?storeId=` / `PUT api/stock/{variantId}?storeId=` | Every active size with this store's stock; set `{ carried, available, reorderThreshold }`. Not carried removes the stock row |

**Rules.** Price above 0 up to 1,00,000 with 2 decimals; MRP not below price. The product's own price and MRP always follow its default size. Past orders keep the price they were placed at. Pictures are stored by a name the server makes, served from `/media` with `nosniff`, and live in `Media:RootPath` (default `src/QuickCommerce.Api/media`, not committed). The apps turn a `/media/...` path into a full address on the same server. The customer app lists per store without a cache, so edits show at once; unscoped lists may take up to 5 minutes.

## Phone OTP and Google sign-in (P8)

Status: built and tested (2026-10-13); migration `AddOtpAndSocialSignIn` applied to the dev database (backup `retail-mobile-app_pre-P8-signin.bak`).

| Route | What |
| --- | --- |
| `POST api/auth/otp/request` | `{ phone }` (10 digits, +91 optional) → `{ resendAfterSeconds, expiresInSeconds }`. **400** not an Indian mobile; **429** too soon / too many (`Retry-After`); **502** the text could not be sent |
| `POST api/auth/otp/verify` | `{ phone, code, fullName? }` → the same token response as login. **401** for every wrong, expired or used code |
| `POST api/auth/google` | `{ idToken }` → token response. **401** token not accepted or email not verified by Google; **503** Google is not configured; **403** the email belongs to a staff account |
| `POST api/account/phone/otp/request` and `/verify` | Signed-in customer: prove a number for their own account. **409** the number is verified on another account |

`GET/PUT api/customer/profile` now also returns `phoneVerified`; a phone-only account has an empty `email`.

**Configuration.** `Otp:Sender` = `Log` (Development only: the code is written to the API log) or `Msg91` (`Otp:Msg91:AuthKey`, `Otp:Msg91:TemplateId`, from a secret store). `Google:ClientIds` = the web OAuth client id(s) the app's ID tokens are made for; empty turns Google sign-in off. Optional: `Otp:CodeKey`, `Otp:LifetimeMinutes`, `Otp:MaxAttempts`, `Otp:ResendSeconds`, `Otp:MaxRequestsPerHour`, `RateLimits:Auth:OtpRequestPerHour` / `OtpVerifyPerHour` / `GooglePerHour`.

## Account deletion and browser access (release readiness)

| Route | What |
| --- | --- |
| `POST api/customer/profile/delete` | `{ confirmation: "DELETE", password? }`. The password is required when the account has one (**400** if wrong). Erases the customer's personal data and closes the account; order and payment records stay without name, phone, address and delivery code. **409** while an order is open or a refund is in progress. Every session ends. No migration |

`GET/PUT api/customer/profile` also returns `hasPassword` (false for accounts made with a phone number or Google).

**Configuration.** `Cors:AllowedOrigins` lists the web pages (for example the admin portal at `https://portal.example.com`) that may call the API from a browser; plain origins only. In Development any page may; elsewhere an empty list means none, and the API warns at start-up. See `docs/RELEASE_AND_SECURITY.md` for the release steps and the security review.

## Reports and the order list (P17)

Status: built and tested (2026-10-14). No migration.

| Route | What |
| --- | --- |
| `GET api/reports?from=2026-10-01&to=2026-10-14&storeId=` | Totals, a row for every day, orders by India hour, top 10 products, by-store table, low stock, and (administrators, all stores) platform income. Store staff always get their own store; `storeId` is for administrators. **400** for an end before the start or more than 92 days; **403** for anyone else |
| `GET api/reports/orders.csv` | The same range as a CSV file: order number, India time, store, status, payment, money. No name, phone, address or id of a customer; cells that start with = + - @ are protected from spreadsheet formulas |

**Definitions.** Days are India days. *Revenue* = total of orders that are Delivered or Completed. *In progress* = open orders; *lost* = cancelled and declined. Unpaid holds are in no count. *New customers* = people whose first order from the stores in scope was placed in the range. *Refunds* = orders whose payment was refunded or is being refunded. *Platform income* (paid and returned offer fees and placement bids) is created-date based.

**Order list.** `GET api/orders` (the portal's Orders page) now comes back with what needs the staff first: new orders, then accepted, packing, ready, on the way, and finished ones last; newest first within each group. The portal reads it again every 15 seconds while the page is open, and marks new orders **NEW**.

## Saved items and ordering again (P18)

Status: built and tested (2026-10-15); migration `AddFavouriteProducts` applied to the dev database (backup `retail-mobile-app_pre-P18-favourites.bak`).

| Route | What |
| --- | --- |
| `GET api/customer/favourites/ids` | Ids of every saved product, newest first (for the app's hearts) |
| `GET api/customer/favourites?storeId=` | The saved products still on sale, newest first, at most 60; with a store each carries its stock |
| `PUT api/customer/favourites/{productId}` | Saves a product; saving twice changes nothing. **404** unknown or off-sale product; **409** at the limit of 200 |
| `DELETE api/customer/favourites/{productId}` | Removes it; removing what is not there changes nothing |

Only a customer has a list (**403** for anyone else), and it is the signed-in customer's own, taken from the token. Deleting the account erases the list.

**Order again** needs no new route: the app sends the order's lines to the existing `POST api/carts/{storeId}/merge` for **the order's own store**. The server adds what it can, never more than the store has, and reports the rest (`Unavailable`, `OutOfStock`, `Reduced`). A cart in another store is emptied first, after the customer agrees.

## Order ratings (P19)

Status: built and tested (2026-10-15); migration `AddOrderRatings` applied to the dev database (backup `retail-mobile-app_pre-P19-ratings.bak`).

| Route | What |
| --- | --- |
| `GET api/customer/orders/{id}/rating` | `{ canRate, canRateDelivery, rating }` for the customer's own order. `canRate` is true once the order is Delivered or Completed and for 14 days after; `canRateDelivery` only when a delivery partner brought it. **404** for an order that is not theirs |
| `PUT api/customer/orders/{id}/rating` | `{ storeStars 1-5, deliveryStars? 1-5, comment? up to 500 characters }`. Gives the rating, or changes it (one per order). **409** not delivered yet / past 14 days; **400** bad stars, a delivery rating without a partner, or a comment with < > or control characters |
| `GET api/reports/ratings?from=&to=&storeId=` | Staff (own store) and administrators (any store, or all): count, average, the spread of 1 to 5 stars, the delivery average, each delivery partner's average, and the latest 30 ratings. The customer is never named |

The store and the partner are taken from the order, never from the request. Deleting an account erases the comment and the link to the customer; the stars stay in the figures. The portal **Reports** page shows the ratings.

## Offers with discounts, pictures and pop-ups; your stores; bought before (P20)

Status: built and tested (2026-10-15); migration `AddOfferDiscountsAndImages` applied to the dev database (additive: new columns on Campaigns and OfferFees, one new table CampaignProducts, one check).

An offer is a message, a percent off chosen products, a picture, or a mix. Staff (own store) and administrators (any stores) create them as before on `POST api/admin/campaigns`; new fields:

| Field | Meaning |
| --- | --- |
| `notify` (default true) | Send the notification. False = a quiet discount (banner and prices only); then the message is optional but a discount is required |
| `discountPercent` 1 to 90, `endsAt`, `productIds` (1 to 50) | Percent off every size of those products at the offer's stores, from the start until `endsAt` (after the start, at most 90 days). Products must exist and be active |
| `imageUrl` | A path returned by `POST api/admin/campaigns/image` (form field `file`; JPEG, PNG or WebP by content, up to 2 MB; saved under /media/offers). Any other path is refused |
| `showOnLoad` | Also shown as a pop-up when the app opens. Store staff pay the extra `loadPaise` fee on top of their normal fee; administrators pay nothing |

Also: `GET api/admin/campaigns/products?search=` (product picker), `POST api/admin/campaigns/{id}/end-discount` (stops a running discount now; 409 when there is none), `PUT api/admin/offer-fees/load` (administrator sets the app-open fee; `loadPaise` is in `GET api/admin/offer-fees` and `.../mine`).

Prices: the server decides. Where offers cover a product the highest percent wins (no stacking). The catalogue shows the lower `price`, the old figure as `mrp` (struck through), `discountPercent` and `offerPercent`; the cart and checkout charge the same price (`PriceChanged` when a discount starts or ends after an item was added, and the receipt keeps the normal price as the MRP). Unpaid, cancelled, not-yet-started and ended offers give no discount.

Customer side:

| Route | What |
| --- | --- |
| `GET api/catalog/offers?storeId=` | Banners for Home: running discounts and picture messages (7 days), with `showOnLoad` |
| `GET api/catalog/offers/{id}` | One offer for the screen a notification or banner opens (title, message, picture, percent, `endsAt`, `running`, stores, `productIds`). 404 when not started, cancelled or unpaid |
| `GET api/customer/recent-stores` | Up to four stores the signed-in customer bought from last (cancelled and unpaid orders do not count) |
| `GET api/customer/stores/{storeId}/bought-before` | What they bought at that store before, newest first, with today's price, offers and stock |

Notifications now carry `campaignId` and `imageUrl`, so tapping an offer notification opens the offer.
