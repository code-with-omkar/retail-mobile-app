# Mini-plan: tasks 1.3 and 1.4 — MRP / discount and Marathi names

**Status:** Implemented and applied to the local dev database `retail-mobile-app` on 2026-10-07. Backup taken first (SQL Server default backup folder, file `retail-mobile-app_pre-1.3-1.4_20261007-171946.bak`, verified with RESTORE VERIFYONLY).
**Date:** 2026-10-07
**Follows:** [CHANGE_PROTOCOL.md](../CHANGE_PROTOCOL.md) section 1 and `copilot-instructions.md` sections 9, 10, 28.

## Decisions already made

| # | Decision |
| --- | --- |
| D14 | The API returns **all translations** in each response (`translations` map). The app picks the language. |
| D15 | The migrations are **schema-only**. Demo MRP and Marathi values come from a separate reviewed dev SQL script. |
| D16 | **No product write endpoint in this work.** Values are set by SQL until a follow-up task (1.12) adds admin editing. |

## What will change and why

The app design needs a discount ("−22%", "You save ₹8") and Marathi names ("Tomato टोमॅटो"). The API has neither: a product has one `Price` and one English `Name`.

1. **1.3 MRP / discount:** a product can carry an optional MRP. The API returns `mrp` and `discountPercent`.
2. **1.4 Translations:** products and categories can carry translated name (and, for products, description) per language. The API returns them in a `translations` map. Search also matches translated names.

### Contract (new customer routes; admin routes untouched)

```jsonc
// GET /api/catalog/categories
[{ "id": "...", "name": "Dairy", "parentCategoryId": null,
   "translations": { "mr": { "name": "दुग्धजन्य" } } }]

// GET /api/catalog/products?page&pageSize&search&categoryId   (exists; fields added)
// GET /api/catalog/products/{id}                              (new)
{ "id": "...", "name": "Tomato", "description": "Fresh red tomatoes",
  "price": 45.00, "mrp": 55.00, "discountPercent": 18,
  "unitOfMeasure": "1 kg", "categoryId": "...", "imageUrl": "...",
  "translations": { "mr": { "name": "टोमॅटो", "description": "ताजे लाल टोमॅटो" } } }
```

Rules:
- `mrp` is always present: `max(Mrp ?? price, price)`. `discountPercent` is `round((mrp − price) / mrp × 100)`, and 0 when there is no discount.
- `translations` lists only languages that have a row. A missing description means "use the English one". English stays in the existing `Name` / `Description` columns.
- Inactive products (and their translations) are never returned. The detail route returns 404 for them.
- `GET /api/products`, `GET /api/products/{id}`, `GET /api/categories`, `ProductResponse`, `CategoryResponse` and their cache keys **do not change**, so the admin portal and existing tests are unaffected.

## Database changes (additive only)

Two migrations, so each can be reviewed and rolled back on its own.

**Migration 1: `AddProductMrp` (task 1.3)**
- `Products.Mrp decimal(18,2) NULL`. `NULL` means "no separate MRP". No backfill and no rewrite of existing rows.
- `CHECK (Mrp IS NULL OR Mrp >= Price)` so a discount can never be negative. A later price rise above MRP is then rejected by the database, which is the intended safeguard.

**Migration 2: `AddCatalogTranslations` (task 1.4)**
- `ProductTranslations(ProductId, Locale nvarchar(10), Name nvarchar(160), Description nvarchar(2000) NULL)`, primary key `(ProductId, Locale)`, foreign key to `Products` (cascade).
- `CategoryTranslations(CategoryId, Locale nvarchar(10), Name nvarchar(100))`, primary key `(CategoryId, Locale)`, foreign key to `Categories` (cascade).
- `nvarchar` stores Devanagari text correctly. Locale codes are lowercase (`mr`, `hi`); validated in the application.

Nothing is dropped, renamed or rewritten. `Down` removes only the new column and tables.

**Existing data:** your dev database `retail-mobile-app` holds 5 products, 4 categories and 1 order. Orders already keep their own snapshot (`UnitPrice`, `ProductNameSnapshot`), so neither migration can change a past order.

## Files likely to change

| Layer | Files |
| --- | --- |
| Domain | `Class1.cs`: `Product.Mrp`; new `ProductTranslation`, `CategoryTranslation` |
| Application | `DTOs/CatalogDtos.cs`; `ICatalogService`; `CatalogService`; `ICommerceStore` (two read methods that fetch translations for a page in one query, no N+1); `CatalogCacheKeys` |
| Infrastructure | `ProductConfiguration`; two new configurations; `QuickCommerceDbContext`; `EfCommerceStore`; `InMemoryCommerceStore`; two migrations plus model snapshot |
| API | `CatalogController`: new `catalog/categories` and `catalog/products/{id}`; existing `catalog/products` returns the added fields |
| Tests | extend `CatalogProductsTests`; new tests for discount maths, translations, translated search, inactive exclusion, 404 |
| Docs | re-export `docs/api/openapi.json`; update `docs/api/README.md`; new `docs/sql/dev-seed-mrp-translations.sql` |
| Mobile | DTOs, repository (switch categories and detail to the new routes), `Category.labelMr`, `Product.descriptionMr`, localized label/description helpers, tests, smoke test |
| Admin portal | none |

## API changes

Additive only: two new routes and added JSON fields on a route created two tasks ago. No existing contract changes.

## UI changes

Real discount stickers and "You save" on product cards and detail. Marathi names and descriptions from the API. Category names localized. The "Today's deals" tile starts working because discounts exist. No theme change.

## Risks and how they are handled

| Risk | Handling |
| --- | --- |
| A migration alters existing data | Both are additive. I read the generated `Up`/`Down` and the snapshot diff before anything runs. |
| A migration fails on your real database | First run the whole migration chain on a **temporary empty database**, then run `Down`, then drop the temp database. Only then touch yours. |
| Data loss on your dev database | Take a `BACKUP DATABASE` first, and apply only after you approve the reviewed migration. |
| Stale cached shapes | The only changed cache entry is the new customer page key; it moves to `v2`. Existing keys (and the test that asserts them) are untouched. |
| Public routes leaking data | Customer DTOs only; inactive products excluded; test asserts exact field names. |
| Slow translated search | Fine at this size. Add an index if the catalog grows. |
| Cart or orders affected | They read `Price` only. Orders use snapshots. |

## Order of work and stop points

1. Domain, configurations, DbContext, in-memory store (code only).
2. Generate migration 1, **read it, stop for review**. Same for migration 2.
3. Temporary-database check: full chain, `Down`, drop.
4. Application, API and tests. `dotnet build` and `dotnet test`.
5. **Stop. You approve applying to `retail-mobile-app`.** Then backup, `dotnet ef database update`, run the dev SQL script, re-export the contract.
6. Mobile changes, `check.ps1`, smoke test, and a look at the app with real discounts and Marathi.

## Tests

- MRP: no MRP gives discount 0; MRP below price is impossible (database check); rounding.
- Translations: returned per locale, missing description, locale case, inactive product excluded, search finds a Marathi name.
- Routes: new routes anonymous; old routes unchanged; detail 404 for inactive or unknown.
- Persistence: new entities in the model; keys and foreign keys as designed.
- Mobile: DTO parsing, fallback to English, localized labels, discount display.

## Dev SQL script (D15)

`docs/sql/dev-seed-mrp-translations.sql` is idempotent and clearly marked **development only; do not run in production**. Suggested demo values: MRP for Tomato (55), Daily Rice (99), Royal Gala Apples (180); Marathi names and descriptions for the 5 products and 4 categories. I will show you the script before running it.

## Not included (recommended separately)

- **Task 1.12, admin editing** of price, MRP, translations and images. There is no product write endpoint today and the admin Products page is a placeholder, so this needs API, authorization and portal work. It pairs naturally with image upload (1.5).
- **Demo users in migrations:** the `DummyUserSeed` migration inserts demo accounts into every database that runs migrations, including production. Worth a separate review before release.
- `CatalogService.GetProductsAsync` still has a leftover `Console.WriteLine`, and the build warns about `Microsoft.OpenApi` 2.3.0 (known high-severity vulnerability).

## Definition of done

`dotnet build` and `dotnet test` pass; migrations reviewed, tested on a temporary database and applied to your dev database with a backup; contract re-exported; mobile quality gate and smoke test pass; the app shows a real discount and a Marathi name from the API.
