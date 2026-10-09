-- DEVELOPMENT DATABASE ONLY. Never run against a shared, staging or production database.
--
-- Adds 250 g and 500 g pack sizes (with stock in every store that stocks the product) to the loose-weight dev products,
-- so the app's pack selector has real variants to show. Run it AFTER migration AddProductVariants has been applied.
-- Idempotent: running it twice changes nothing the second time.
-- With sqlcmd use the -I flag (quoted identifiers), which the filtered unique index on ProductVariants requires:
--   sqlcmd -S localhost -E -C -I -d <dev database> -b -i docs/sql/dev-seed-variants.sql
--
-- Prices: each pack has its own price, a little above the pro-rata share (smaller packs cost slightly more per kg):
--   500 g = ceiling(1 kg price x 0.52), 250 g = ceiling(1 kg price x 0.28); MRP the same way when the 1 kg pack has one.
-- Stock: 2x the 1 kg stock for 500 g packs, 3x for 250 g packs, per store.
-- Ordering: 250 g, 500 g, 1 kg (the 1 kg pack stays the default).

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

IF OBJECT_ID(N'dbo.ProductVariants') IS NULL
    THROW 50100, 'ProductVariants does not exist: apply migration AddProductVariants first.', 1;

DECLARE @packs TABLE (Suffix nvarchar(10), Label nvarchar(40), Factor decimal(5, 2), StockFactor int, SortOrder int);
INSERT @packs VALUES (N'-500G', N'500 g', 0.52, 2, 1), (N'-250G', N'250 g', 0.28, 3, 0);

-- 1 kg stays the default and sorts last.
UPDATE v SET v.SortOrder = 2
FROM dbo.ProductVariants v
JOIN dbo.Products p ON p.Id = v.ProductId
WHERE v.IsDefault = 1 AND v.Label = N'1 kg' AND p.Name IN (N'Tomato', N'Potato', N'Royal Gala Apples', N'Daily Rice') AND v.SortOrder <> 2;

INSERT INTO dbo.ProductVariants (Id, ProductId, Sku, Label, Price, Mrp, SortOrder, IsDefault, IsActive)
SELECT NEWID(), d.ProductId, LEFT(d.Sku + k.Suffix, 80), k.Label,
       CEILING(d.Price * k.Factor),
       CASE WHEN d.Mrp IS NULL THEN NULL ELSE CEILING(d.Mrp * k.Factor) END,
       k.SortOrder, 0, 1
FROM dbo.ProductVariants d
JOIN dbo.Products p ON p.Id = d.ProductId
CROSS JOIN @packs k
WHERE d.IsDefault = 1 AND d.Label = N'1 kg' AND p.Name IN (N'Tomato', N'Potato', N'Royal Gala Apples', N'Daily Rice')
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductVariants x WHERE x.ProductId = d.ProductId AND x.Label = k.Label);

INSERT INTO dbo.StoreVariantInventory (StoreId, VariantId, AvailableQuantity, ReorderThreshold)
SELECT s.StoreId, v.Id, s.AvailableQuantity * k.StockFactor, s.ReorderThreshold
FROM dbo.ProductVariants v
JOIN dbo.ProductVariants d ON d.ProductId = v.ProductId AND d.IsDefault = 1
JOIN dbo.StoreVariantInventory s ON s.VariantId = d.Id
JOIN @packs k ON k.Label = v.Label
WHERE v.IsDefault = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.StoreVariantInventory x WHERE x.StoreId = s.StoreId AND x.VariantId = v.Id);

COMMIT;

SELECT p.Name, v.Label, v.Price, v.Mrp, v.IsDefault, v.SortOrder,
       (SELECT SUM(i.AvailableQuantity) FROM dbo.StoreVariantInventory i WHERE i.VariantId = v.Id) AS TotalStock
FROM dbo.ProductVariants v JOIN dbo.Products p ON p.Id = v.ProductId
ORDER BY p.Name, v.SortOrder;
