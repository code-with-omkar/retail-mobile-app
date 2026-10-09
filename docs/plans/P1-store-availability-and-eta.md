# Mini-plan: tasks 1.2, 1.6 and 1.10 — stock status, delivery time, nearest store

**Status:** Implemented 2026-10-07. No database change was needed.
**Date:** 2026-10-07
**Follows:** [CHANGE_PROTOCOL.md](../CHANGE_PROTOCOL.md) section 1, and the previous plan [P1-mrp-and-translations.md](./P1-mrp-and-translations.md).

## Decisions already made

| # | Decision |
| --- | --- |
| D17 | Delivery time comes from **configuration only** (base minutes + minutes per km). No database change. A per-store override waits for the admin editor (task 1.12). |
| D18 | The public API reveals only `inStock` and `lowStock` flags, **never exact quantities**. |
| D19 | Until phase P4 (real addresses and device location), the app uses a **default demo location** from its config. |

## What will change and why

The Home screen still shows placeholder data: store "Sharma Fresh Mart", "1.2 km", "12 min", and every product is treated as in stock.

1. **1.2 Stock status:** the product list and detail can be asked for one store's stock and return `inStock` and `lowStock`.
2. **1.6 Delivery time:** the API computes an estimate from configuration, never hardcoded in code or in the app.
3. **1.10 Nearest store:** a new customer route finds the nearest serviceable store and returns its name, distance and estimate. Home and the product page show the real store and ETA, and cards show "Out of stock" per store.

### Contract (new customer route; existing routes unchanged)

```jsonc
// GET /api/catalog/stores/nearest?latitude=19.060&longitude=72.830      (anonymous)
{ "id": "...", "name": "Harbor Point Dark Store", "distanceKm": 5.4, "estimatedMinutes": 19, "serviceRadiusKm": 8 }
// 404 { success:false, message:"No serviceable store near this location" } when none covers the point

// GET /api/catalog/products?...&storeId=<id>     and   GET /api/catalog/products/{id}?storeId=<id>
{ ...existing fields..., "inStock": true, "lowStock": false }
```

Rules:
- `inStock` is `AvailableQuantity > 0` for that store. `lowStock` is `0 < AvailableQuantity <= ReorderThreshold`.
- **Without `storeId`, both fields are `null`** (unknown). The app treats `null` as in stock.
- An unknown or inactive `storeId` returns 404. No exact quantity is ever returned.
- Estimate: `estimatedMinutes = BasePrepMinutes + ceil(distanceKm × MinutesPerKm)`.
- **Staleness:** stock is display-only. The checkout re-checks stock under the existing concurrency control, so an item shown "in stock" can still be rejected at checkout (handled in P5).

### Configuration (new `Delivery` section, with validation)

```json
"Delivery": { "BasePrepMinutes": 8, "MinutesPerKm": 2 }
```

Both must be positive; the app fails fast at startup if they are not. These defaults are placeholders for you to tune.

With the demo location (Bandra West, 19.0596, 72.8295) and your 3 seeded stores:

| Store | Distance | Serviceable | Estimate |
| --- | --- | --- | --- |
| Harbor Point Dark Store | 5.4 km | yes (radius 8) | 19 min |
| North Star Fulfillment | 7.5 km | yes (radius 9) | 23 min |
| Cedar Market Hub | 10.2 km | no (radius 7) | n/a |

Harbor Point is chosen. It has 0 Farm Milk, so that product shows "Out of stock" there, a real demonstration of 1.2.

## Database changes

**None.** Stock already lives in `StoreInventory`; the estimate comes from configuration. No migration.

## API changes

Additive only:
- new `GET /api/catalog/stores/nearest`
- optional `storeId` on `catalog/products` and `catalog/products/{id}`
- new nullable `inStock` / `lowStock` fields on the customer product response
- the page cache key moves from `v2` to `v3` because the response shape changes. The cached copy never contains stock; availability is **overlaid fresh on every request**, so stock changes show immediately.

The existing `GET /api/stores/nearest` (used by other code), the admin routes and the checkout are untouched.

## Files likely to change

| Layer | Files |
| --- | --- |
| Application | `CatalogDtos.cs` (new `NearestStoreResponse`, stock fields); `ICatalogService`/`CatalogService`; new `DeliveryEstimator` + `DeliverySettings`; `ICommerceStore` (one new read: stock for a store and a set of products, a single query, no N+1); `CatalogCacheKeys` |
| Infrastructure | `EfCommerceStore`, `InMemoryCommerceStore` (read-only query) |
| API | `CatalogController` (new route, `storeId` param); settings binding and validation in the existing API registration extension; `appsettings.json` / `appsettings.Development.json` `Delivery` section |
| Tests | new tests (below); the existing ones stay untouched |
| Docs | re-export `openapi.json`; update `docs/api/README.md` |
| Mobile | DTOs; repository (`nearestStore`, `storeId` on products); a `deliveryLocationProvider` (interim) and `nearestStoreProvider`; Home hero card and product page use real values; product cards show out-of-stock; app config gets the demo coordinates; Marathi strings; tests |
| Admin portal | none |

## UI changes

- **Home hero card:** real store name, distance and ETA (loading placeholder, and a clear "We don't deliver here yet" state if no store covers the location).
- **Product card and page:** out-of-stock shown per store (the cards already have this state), and "Only a few left" when `lowStock`.
- **Product page "Sold by":** the real store name.
- The app **rounds coordinates to 3 decimals (about 110 m)** before sending them. Precise home locations would otherwise end up in server and IIS request logs.
- No theme change.

## Risks and handling

| Risk | Handling |
| --- | --- |
| Revealing stock levels to competitors | Flags only, never quantities (D18); test asserts the field names. |
| Precise location in request logs | Coordinates rounded to about 110 m by the app; the API does not log query strings beyond what it does today. |
| Stale "in stock" | Fresh overlay on every request; checkout remains the authority (P5). |
| Public endpoint abuse | No rate limiting exists today. Recorded as a P9 hardening item; the route is cheap (reads three small tables). |
| Wrong store chosen from a placeholder location | Location is a clearly marked interim default until P4. |
| Misconfigured delivery settings | Validated at startup. |

## Order of work and stop points

1. Application: `DeliveryEstimator`, settings, DTOs, service methods, store read. Config binding in the API.
2. API route and tests. `dotnet build`, `dotnet test`.
3. Live check against your database (read-only), re-export the contract.
4. Mobile: DTOs, repository, providers, Home and product page, tests, `check.ps1`.
5. Smoke test, and a look at the app: the real store and ETA on Home, Farm Milk out of stock.

**No database stop point is needed** because nothing is migrated. I will not touch your data.

## Tests

- **Estimator:** the formula, ceiling at fractions, validation of bad settings.
- **Nearest route:** anonymous; nearest of several; skips inactive and out-of-radius stores; 404 when none; includes distance and estimate.
- **Stock:** `inStock` false at 0; `lowStock` at or below the threshold and above 0; null without `storeId`; 404 for unknown or inactive store; no quantity field exposed; fresh stock visible despite the cached page.
- **Mobile:** DTO parsing, repository, Home shows the store and ETA, no-store state, out-of-stock card, coordinate rounding, `lowStock` label.
- **Live smoke:** nearest store resolves for the demo location; Farm Milk is out of stock at Harbor Point.

## Not included (recommended separately)

- Per-store ETA override column and admin editing (task 1.12).
- Real addresses and device location (P4). The cart's store switching (P5).
- Rate limiting on public routes (P9 hardening).

## Definition of done

Tests and analyzers pass; contract re-exported; the app on the live API shows the real store, distance and ETA, "Out of stock" for Farm Milk, and no placeholder store data remains on Home.
