# Mini-plan: choose a store, see that store's items

Status: **implemented and verified** (2026-10-07). Approved as written; the two open details were settled as: switching only from the Home card and the Choose a store screen, and stores that carry nothing are hidden. Follows `docs/copilot-instructions.md` and `docs/CHANGE_PROTOCOL.md`.

## Why

QuickCart sells from dark stores. Today the app silently uses one "nearest store" for a fixed demo location, and shows every product with stock flags. Customers should see which stores deliver to them, choose one, and then see **that store's** catalogue.

## Decisions (made 2026-10-07)

| # | Decision |
| --- | --- |
| D24 | The customer **picks a store from the stores that deliver to the delivery location**. The nearest is pre-selected; the choice is remembered. |
| D25 | A store's catalogue is **the products it carries** (has an inventory row). Out-of-stock items stay visible but cannot be added. |
| D26 | **One cart belongs to one store.** Switching store with items in the cart asks for confirmation and empties the cart. |

## What changes

### API (additive, no migration, existing routes keep their behaviour)

1. `GET /api/catalog/stores?latitude&longitude` (anonymous, new): active stores whose service radius covers the point, nearest first. Each: `id, name, address, distanceKm, estimatedMinutes, serviceRadiusKm`. Same validation and delivery-estimate rules as `catalog/stores/nearest`. Empty list (not 404) when none serves the point.
2. `GET /api/catalog/products` gets an optional `carriedOnly=true`. With a `storeId` it returns only products that store carries, with `totalCount`/paging for that set. Without `carriedOnly` nothing changes. Stock flags stay fresh on every request; the shared cached page is not reused for store-filtered requests.
3. `GET /api/catalog/categories` gets an optional `storeId`: only categories with at least one product the store carries, so a store never shows empty categories.

Server-side authorisation is unchanged: this is public catalogue data and returns no quantities. Checkout and cart keep validating the store on the server (P5).

### Mobile

- `selectedStoreProvider`: saved store id in preferences; on start it is kept if the store still serves the location, otherwise the nearest is chosen automatically. No store serving the location keeps the existing "We do not deliver here yet" state.
- New **Choose a store** screen (`/stores`): one card per store (name, distance, delivery minutes, "Nearest" and "Selected" markers), opened from the Home store card and from a store chip in the header.
- Every catalogue provider (categories, Home, category, search, related items) uses the selected store and sends `carriedOnly`. Out-of-stock cards are greyed with the existing "Out of stock" label.
- Cart: shows "From {store}". Choosing another store with items in the cart shows "Your cart will be emptied" and only switches on confirm.
- The store list uses the same delivery location as everything else, so phase P4 (real addresses) needs no change here.
- English and Marathi strings; tests for the provider logic, the screen, the cart-switch dialog and the new API calls.

### Not in this change

- **Opening hours / open-closed**: stores have no hours in the database. Showing "closed" would need a schema change; propose it as a later task.
- **Per-store prices**: prices stay global.
- **Cart and checkout against the server**: P5. The local cart is store-aware as above; P5 wires it to the API.

## Safety and compatibility

- No database change, no migration. Index use: the lookup is on `StoreInventory (StoreId, ProductId)`, the primary key.
- Old app builds keep working: existing parameters and responses are unchanged.
- No new infrastructure, no secrets, no auth change.

## Tests

Backend: store list (radius, ordering, empty, invalid coordinates), `carriedOnly` paging and counts, categories with `storeId`, cache not reused across stores, EF integration on a throwaway database. Mobile: DTOs, selection rules, screen, cart switch, live smoke test.

## Rollback

Revert the commit. Nothing persistent changed on the server; the app's saved store id is ignored by older builds.
