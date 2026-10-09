-- ROLLBACK HELPER for phase P3 (product variants). Read docs/plans/P3-product-variants.md first.
--
-- After the P3 migration, stock is read and written in dbo.StoreVariantInventory. The old table dbo.StoreInventory was left
-- untouched, so it is frozen at the moment of the migration. If you must put the PREVIOUS application version back (code
-- rollback) WITHOUT running the migration's Down, run this script first: it copies the current stock of every default variant
-- back into dbo.StoreInventory, so sales made while variants were live are not lost.
--
-- (Running the migration's Down does the same copy automatically. This script is for a code-only rollback.)
--
-- Only default-variant stock can be represented in the old table; other pack sizes have no home in the old model.
-- Run with sqlcmd -I (quoted identifiers) or in SSMS. Take a backup first. Read-only check first: run the SELECT below, then the UPDATE.

SET NOCOUNT ON;

-- What would change:
SELECT p.Name, s.Name AS Store, si.AvailableQuantity AS OldTable, x.AvailableQuantity AS Variants
FROM dbo.StoreInventory si
JOIN dbo.ProductVariants v ON v.ProductId = si.ProductId AND v.IsDefault = 1
JOIN dbo.StoreVariantInventory x ON x.StoreId = si.StoreId AND x.VariantId = v.Id
JOIN dbo.Products p ON p.Id = si.ProductId
JOIN dbo.Stores s ON s.Id = si.StoreId
WHERE si.AvailableQuantity <> x.AvailableQuantity
ORDER BY p.Name, s.Name;

BEGIN TRAN;

UPDATE si SET si.AvailableQuantity = x.AvailableQuantity
FROM dbo.StoreInventory si
JOIN dbo.ProductVariants v ON v.ProductId = si.ProductId AND v.IsDefault = 1
JOIN dbo.StoreVariantInventory x ON x.StoreId = si.StoreId AND x.VariantId = v.Id;

PRINT CONCAT('Rows synced: ', @@ROWCOUNT);

COMMIT;
