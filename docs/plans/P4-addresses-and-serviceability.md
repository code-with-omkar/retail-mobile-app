# Mini-plan: addresses and serviceability (P4)

Status: **approved 2026-10-09. Backend (4.1, 4.2, 4.7, 4.8, 4.9) built, verified and applied to the dev database on 2026-10-09** (backup retail-mobile-app_pre-P4-addresses taken and verified first; 15 migrations; live checks passed). App work (4.3, 4.5, 4.6) built and tested 2026-10-09 (221 tests; checked on the phone 2026-10-09); 4.4 (Google Maps) waits for your API key.

## Why

Today the app has no real idea where the customer is. The delivery location is a fixed demo point (Bandra West, decision D19), the address list is hard-coded, the map is a drawing, and the server accepts any address and coordinates at checkout without checking that the store delivers there. P4 makes the address real: a saved address list on the server, a picker with a real map, current location, and a serviceability rule enforced on the server and explained in the app.

## Decisions

| # | Decision |
| --- | --- |
| D19 | The interim demo location is **replaced** by the customer's chosen address or location. (Retires the config fallback outside development.) |
| D30 | Map is **Google Maps + Places**. The picker sits behind an interface, so the rest of P4 does not wait for the key, and the app degrades gracefully without one (see "Key and setup"). |
| D31 | **Guests can choose a delivery location**, kept on the device only; it is never sent as an account address. Signing in unlocks saved addresses on the server. |
| D32 | A saved address holds **label, address line, flat/building, landmark, receiver name and receiver phone**, plus coordinates and a default flag. Phone and name are personal data: stored only for the owner, never logged, and shown to staff only through the order snapshot (P5), not through this API. |
| D33 | A customer can save **at most 10 addresses** (a limit that can change in configuration, default 10). |
| D34 | **Serviceable** means at least one active store, in the customer's organisation, whose delivery radius covers the point and that carries at least one active product. The same rule the store list already uses. |

## Today

- Server: nothing stores addresses. `CheckoutRequest` carries a free-text `DeliveryAddress`, `Latitude` and `Longitude`, validated only for length and range.
- **Gap:** `CheckoutService` does not check that the chosen store serves those coordinates, so an out-of-range address can be ordered. Closing this is part of P4 (task 4.9), because it is a server-side rule.
- Server: the "does a store serve this point" logic exists (`catalog/stores`, `catalog/stores/nearest`) but returns no reason and nothing reuses it for checkout.
- App: `addressProvider` is a hard-coded address; the picker draws a fake map; the store lookup uses a config coordinate; there is no location permission.

## Design

### Database (additive, one new table)

`CustomerAddresses`: `Id`, `CustomerId` (FK to `Customers`, cascade), `Label` (max 40), `Line` (max 300, the formatted address from the map or search), `FlatOrBuilding` (max 120), `Landmark` (max 120), `Latitude`, `Longitude` (checked ranges), `ReceiverName` (max 120), `ReceiverPhone` (max 20, normalised like the account phone), `IsDefault`, `CreatedAt`, `UpdatedAt`. Indexes: by customer; a filtered unique index allowing **one default per customer**. No existing table or row changes, so rollback is dropping the table.

Migration rules as before: schema only, Up and Down reviewed, applied first to a disposable database, then to the dev database after a verified backup, never automatically in production.

### API

All customer routes use the `Orders` policy; the customer id comes from the token, never from the request.

- `GET api/customer/addresses`: the caller's addresses, default first. Each carries `serviceable` (computed now, so a change in store coverage shows at once).
- `POST api/customer/addresses`: create. The first address becomes the default. Over the limit: 409. Invalid fields: 400 with per-field messages.
- `PUT api/customer/addresses/{id}`: update (not the default flag).
- `POST api/customer/addresses/{id}/default`: make it the default; the previous default is cleared in the same transaction.
- `DELETE api/customer/addresses/{id}`: delete; deleting the default promotes the most recently updated remaining address.
- Another customer's id returns **404, not 403**, so existence is not revealed.
- `GET api/catalog/serviceability?latitude&longitude` (public, for guests and for the picker): `{ serviceable, reason, nearestDistanceKm }`, where `reason` is `Serviceable`, `OutsideServiceArea` (no store's radius covers the point) or `NoStoreAvailable` (a radius covers it but no store carries products). Coordinates are validated like the store list.
- **Checkout (task 4.9):** the commit checks that the chosen store's radius covers the delivery coordinates and returns **409 with `reason`** when it does not. Nothing else about checkout changes; saved-address ids at checkout are P5.
- Privacy: request logging already records no bodies; receiver phone is never logged, and not returned to anyone but the owner.

### Mobile

- **Location:** `geolocator` for permission and a one-shot position. Handles denied, permanently denied (explain, then open settings) and location services off, with a manual fallback. Android 12+ approximate versus precise location works because only coarse accuracy is needed to find stores; the pin is then fine-tuned.
- **Picker:** a `LocationPicker` interface with a Google Maps implementation (`google_maps_flutter`: draggable pin, current-location button) and Places autocomplete for search; reverse geocoding of the pin through the platform geocoder (`geocoding`, no extra key). Tests use a fake picker.
- **Screens:** Saved addresses (list, default marker, add, edit, delete, a not-serviceable badge), Address editor (map, search, flat, landmark, receiver, label, default), and a first-run "Where should we deliver?" sheet for people with no location yet.
- **Selected address drives everything:** a signed-in customer's default (or last chosen) address; a guest's device-only location. The delivery location provider replaces the config coordinate; stores, ETA, Home header, checkout and orders all follow it.
- **Address change with a full cart:** if the change makes the cart's store stop serving the new address, the customer is told and asked before the cart is emptied (the existing store-switch rule).
- **Not serviceable:** a clear state on Home, in the picker and on the checkout button ("We do not deliver here yet", with "Try another address"); checkout is blocked in the app and on the server. English and Marathi.
- Local state: the selected address id and a guest location are kept in preferences; nothing sensitive beyond coordinates rounded to 3 decimals (about 110 m) is sent to store and catalogue calls, as today.

### Key and setup (needs you)

Google Maps needs a Google Cloud project, **billing turned on**, the **Maps SDK for Android** and **Places API (New)** enabled, and an API key restricted to the app (package `com.quickcart.quickcart_customer` plus the SHA-1 of the debug and release signing keys). I cannot create it. Rules:

- The key lives only in an untracked local file (`mobile/android/secrets.properties`) and a local `--dart-define-from-file` for the Places call. It is never committed. A new test fails the build if something that looks like a Google API key appears in the repository (task 4.8), like the existing credentials guard.
- Set a billing budget alert in Google Cloud so a leaked or abused key cannot run up costs unnoticed.
- **Without a key** the app still works: current location, the typed form and a coordinate-based fallback are available, the map area explains that maps need setup, and tests run with the fake picker. So P4's server work and everything else can be finished and verified before the key exists, and the map lights up when you add it.

## Not in this change

- Saved-address ids at checkout and the order's address snapshot: **P5** (checkout keeps taking text and coordinates now).
- Delivery slots, per-area fees and polygon delivery zones (radius only, as today).
- Address verification by phone/OTP, address sharing, and import from other apps.
- Admin viewing of customers' addresses (staff see the address only on an order, in P5/P7).

## Impact on consumers

| Consumer | Impact |
| --- | --- |
| Customer app (current build) | Works unchanged; its checkout now gets a 409 for an out-of-range address, which it already handles as a conflict. |
| Admin portal | None. |
| Orders | Unchanged; the existing address text and coordinates are still stored on the order. |

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Address data is personal data | Owner-only access, 404 for others, no logging, limit of 10, tested scope rules. |
| API key leakage or abuse | Restricted key, untracked file, repository guard test, billing budget alert. |
| Location permission denied | Manual fallback and explanation; never blocks browsing. |
| Coverage changes after an address is saved | `serviceable` is computed on every read, and checkout re-checks on the server. |
| Cart in a store that no longer serves the new address | Explicit confirmation before the cart is emptied. |
| Google costs | Places calls only on user search with a minimum text length and debounce; reverse geocoding uses the free platform geocoder. |

## Tasks

| Task | What |
| --- | --- |
| 4.1 | Migration `AddCustomerAddresses` and the CRUD routes with default handling and the limit |
| 4.2 | Serviceability service and `catalog/serviceability`, with reasons |
| 4.9 | Checkout rejects an address the store does not serve (409 with reason) |
| 4.3 | Location permission and current position |
| 4.4 | Google Maps picker and Places search behind the picker interface (needs your key) |
| 4.5 | Saved-address screens; selected address replaces the demo location; guest location |
| 4.6 | Not-serviceable states and messages in English and Marathi; cart-store confirmation |
| 4.7 | Tests: scope (another customer's address is 404), default uniqueness, limit, boundary coordinates, checkout rule, parallel default changes on SQL Server |
| 4.8 | Repository guard against API keys in source |

## Proposed order of work

1. Sign-off on this plan.
2. Backend: migration on a disposable database, routes, serviceability, checkout rule, tests (4.1, 4.2, 4.9, 4.7, 4.8); then backup, apply to the dev database, live checks.
3. App without a map: location permission, address list and editor with the typed form, selected address, not-serviceable states (4.3, 4.5, 4.6).
4. Google Maps and Places once you have created the key (4.4), then a check on a phone.

## Rollback

The migration's Down drops the new table; no existing data was touched. The app reverts to the previous build, which uses the config location and ignores saved addresses. The checkout rule is a small code change that can be reverted independently.
