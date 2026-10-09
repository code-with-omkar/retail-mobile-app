# Mini-plan: product variants (pack sizes)

Status: **backend implemented, verified and applied to the dev database on 2026-10-08** (backup retail-mobile-app_pre-P3-variants taken and verified first; 14 migrations; dev packs loaded with docs/sql/dev-seed-variants.sql; live checks passed). Mobile (task 3.6) is built and tested (141 tests, live smoke test); **P3 is complete apart from a check on a phone**. Admin variant management waits for product editing (D29).

## Why

The customer app shows pack sizes (250 g, 500 g, 1 kg) but they are a client-side mock: one price per product, a pack is price x factor, stock is per product, and the cart is keyed by product. Real catalogues need a price, an MRP and stock **per pack size**, and orders must remember which pack was bought at what price.

## Decisions

| # | Decision |
| --- | --- |
| D2 | Add `ProductVariant` with an additive migration and a default variant, so old clients keep working (existing). |
| D27 | **Stock is tracked per variant** per store (for example 12 x 500 g packs), not as a pool in base units. |
| D28 | **Each variant has its own price and MRP.** Not computed from a pack factor. |
| D29 | **Admin portal variant management is not part of P3.** It comes with product editing (task 1.12) as one piece, because there is no product write API yet (D16). P3 loads dev variants with a script. |

## Today (what depends on the product)

- `StoreInventory`: primary key (StoreId, ProductId), `AvailableQuantity`, `ReorderThreshold`, `RowVersion` concurrency token.
- `CartItem`: key (CartId, ProductId), name and unit price snapshots. Cart routes: `api/carts/{storeId}/items[/{productId}]`.
- `OrderItem`: its own Id, `ProductId`, name and unit price snapshots. Order lines come in as `OrderLineRequest(ProductId, Quantity)`.
- Checkout and admin order creation decrement `StoreInventory` inside a transaction (two code paths: `EfCommerceStore` checkout commit and `TryCreateOrderAsync`).
- Customer catalogue routes return `price`, `mrp`, `unitOfMeasure` and stock flags per product.

## Design

### Database (additive, expand then contract)

1. **`ProductVariants`** (new): `Id`, `ProductId` (FK, restrict), `Sku` (unique), `Label` (for example "500 g", max 40), `Price`, `Mrp` (null = no discount; check `Mrp >= Price`, same rule as products), `SortOrder`, `IsDefault`, `IsActive`. A filtered unique index allows exactly one default variant per product.
2. **Backfill**: one default variant per existing product, copying its price, MRP, unit of measure (as label) and a derived SKU. Idempotent; never touches the product row.
3. **`StoreVariantInventory`** (new, replaces `StoreInventory` for reads and writes): key (StoreId, VariantId), `AvailableQuantity`, `ReorderThreshold`, `RowVersion`. Backfilled one row per existing `StoreInventory` row, pointing at that product's default variant. The old table is **left in place, untouched**, as the rollback safety copy.
4. **`CartItem`**: add `VariantId` (backfilled to the default variant), key becomes (CartId, VariantId). `ProductId` stays.
5. **`OrderItem`**: add nullable `VariantId`, `VariantLabelSnapshot`, `UnitMrpSnapshot`. Existing orders keep null and display with the product unit as today. Nothing is rewritten.
6. **Contract step (later, separate approval)**: drop `StoreInventory` once variants have run in production and the data is verified.

Migration rules: schema and backfill only, Up and Down reviewed, applied first to a disposable database, then to the dev database after a verified backup, never automatically in production. The backfill is checked on a copy of the data (row counts, every product has exactly one default variant, every inventory row has a counterpart).

### API (additive; old clients unaffected)

- Catalogue products gain `variants: [{ id, label, price, mrp, discountPercent, isDefault, inStock, lowStock }]`. The existing `price`, `mrp`, `unitOfMeasure`, `inStock` and `lowStock` keep meaning "the default variant", so a client that ignores variants behaves as before. Stock flags still never expose quantities, and are per variant when a `storeId` is given.
- Cart: `AddCartItemRequest` gains optional `variantId` (absent = default variant). Update and remove take an optional `?variantId=` (absent = default variant). Responses add `variantId` and `variantLabel`.
- Orders: `OrderLineRequest` gains optional `variantId`. Checkout validates the variant belongs to the product, is active, re-checks its price against the cart snapshot and its stock in a transaction, and writes the snapshots (name, label, price, MRP).
- Stock decrement moves to `StoreVariantInventory` in both code paths, keeping the `RowVersion` concurrency token.
- Server-side rules unchanged: customer id from the token, store validation, price re-check at checkout.

### Mobile

- Variants parsed into the product (`PackOption`: id, label, price, MRP, stock flags); the pack selector is built from them and the weight-factor maths is removed.
- Cart is keyed by variant id; lines show the pack label; the default variant is what the quick "+" button adds.
- Seed data gets variants too, so offline mode and tests keep working.
- English and Marathi: pack labels such as "500 g" are language-neutral. Variant label translations are not included (units are the same in both languages).

### Dev data

`docs/sql/dev-seed-variants.sql` (dev only, idempotent) adds 250 g / 500 g / 1 kg variants and stock for the loose products in the dev database, like the earlier MRP and translation script.

## Not in this change

- **Admin portal variant management** (task 3.5): deferred with product editing (D29). Until then the admin products page keeps showing the default variant's price through the unchanged `/api/products`.
- **Variant images, variant translations, per-variant tax or barcode.**
- **Dropping `StoreInventory`**: a separate, later approval.
- **Wiring the app cart to the server**: P5.

## Impact on consumers

| Consumer | Impact |
| --- | --- |
| Customer app (current build) | None. All existing fields and routes behave as before. |
| Admin portal | None required. `/api/products` unchanged. Inventory screens, if any, keep working because they read the unchanged API shapes. |
| Order history | Old orders unchanged. New orders also show the pack label. |
| Future cart and checkout (P5) | Built on variant ids from the start. |

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Overselling the last unit of a variant | Transactional decrement with `RowVersion`; a parallel-checkout test on SQL Server. |
| Stock drift if the code is rolled back after cutover | Old table kept; a reverse-sync script (variant stock back to `StoreInventory`) is written and tested with the migration. |
| Primary key change on `CartItem` | Carts are transient; checked on a copy; `ProductId` is kept. |
| Backfill misses a product | Verification queries in the plan's checklist; migration fails loudly rather than guessing. |

## Tests

Backend: default-variant backfill logic, catalogue variants and old-field compatibility, cart add/update/remove with and without `variantId`, wrong-variant and inactive-variant rejection, checkout price re-check per variant, order snapshots unchanged after a later price edit, parallel checkout on the last unit (disposable SQL Server), old routes unchanged. Mobile: DTOs, pack selector from variants, cart keyed by variant, seed variants, live smoke test.

## Rollback

Before the contract step: revert the code, run the reverse-sync script, and leave the new tables (empty of consequence). The migration's Down removes the new tables and columns; no existing data was altered.

## Proposed order of work

1. Sign-off on this plan (3.1).
2. Disposable-database migration with backfill and verification (3.2).
3. API: catalogue, cart, orders, snapshots (3.3, 3.4).
4. Backend tests including concurrency (3.7).
5. Backup, apply to the dev database, live checks.
6. Mobile pack selector and cart (3.6), smoke test, phone check.
