/*
  DEVELOPMENT ONLY. DO NOT RUN IN STAGING OR PRODUCTION.

  Demo MRP values and Marathi text for the 5 seeded products and 4 seeded categories, so the app can show
  discounts and Marathi names before an admin editor exists (plan task 1.12).

  Requires the migrations AddProductMrp and AddCatalogTranslations to be applied first.
  Safe to run more than once: it only fills values and never deletes anything.
  Save/run as UTF-8 (sqlcmd -f 65001) so the Marathi text is stored correctly.

  Run:  sqlcmd -S localhost -E -C -d retail-mobile-app -f 65001 -i docs/sql/dev-seed-mrp-translations.sql
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.Products', 'Mrp') IS NULL OR OBJECT_ID('dbo.ProductTranslations') IS NULL OR OBJECT_ID('dbo.CategoryTranslations') IS NULL
BEGIN
    RAISERROR('Apply the AddProductMrp and AddCatalogTranslations migrations first.', 16, 1);
    RETURN;
END;

BEGIN TRANSACTION;

-- MRP (must be >= Price; enforced by CK_Products_Mrp_GreaterOrEqual_Price). Only fills products that have none yet.
UPDATE dbo.Products SET Mrp = 55  WHERE Id = '20000000-0000-0000-0000-000000000001' AND Mrp IS NULL; -- Tomato 45 -> 18% off
UPDATE dbo.Products SET Mrp = 180 WHERE Id = '20000000-0000-0000-0000-000000000004' AND Mrp IS NULL; -- Royal Gala Apples 149 -> 17% off
UPDATE dbo.Products SET Mrp = 99  WHERE Id = '20000000-0000-0000-0000-000000000005' AND Mrp IS NULL; -- Daily Rice 89 -> 10% off

-- Product translations (Marathi). Inserts a row only when that product has no 'mr' row yet.
INSERT INTO dbo.ProductTranslations (ProductId, Locale, Name, Description)
SELECT v.ProductId, 'mr', v.Name, v.Description
FROM (VALUES
    (CONVERT(uniqueidentifier, '20000000-0000-0000-0000-000000000001'), N'टोमॅटो',     N'ताजे लाल टोमॅटो'),
    (CONVERT(uniqueidentifier, '20000000-0000-0000-0000-000000000002'), N'बटाटा',      N'रोजच्या स्वयंपाकासाठी बटाटे'),
    (CONVERT(uniqueidentifier, '20000000-0000-0000-0000-000000000003'), N'दूध',        N'पाश्चराइज्ड फुल क्रीम दूध'),
    (CONVERT(uniqueidentifier, '20000000-0000-0000-0000-000000000004'), N'सफरचंद',     N'कुरकुरीत आणि नैसर्गिकरित्या गोड'),
    (CONVERT(uniqueidentifier, '20000000-0000-0000-0000-000000000005'), N'तांदूळ',     N'प्रत्येक जेवणासाठी लांब दाण्याचा तांदूळ')
) AS v(ProductId, Name, Description)
WHERE EXISTS (SELECT 1 FROM dbo.Products p WHERE p.Id = v.ProductId)
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductTranslations t WHERE t.ProductId = v.ProductId AND t.Locale = 'mr');

-- Category translations (Marathi).
INSERT INTO dbo.CategoryTranslations (CategoryId, Locale, Name)
SELECT v.CategoryId, 'mr', v.Name
FROM (VALUES
    (CONVERT(uniqueidentifier, '10000000-0000-0000-0000-000000000001'), N'किराणा'),
    (CONVERT(uniqueidentifier, '10000000-0000-0000-0000-000000000002'), N'भाज्या'),
    (CONVERT(uniqueidentifier, '10000000-0000-0000-0000-000000000003'), N'फळे'),
    (CONVERT(uniqueidentifier, '10000000-0000-0000-0000-000000000004'), N'दुग्धजन्य')
) AS v(CategoryId, Name)
WHERE EXISTS (SELECT 1 FROM dbo.Categories c WHERE c.Id = v.CategoryId)
  AND NOT EXISTS (SELECT 1 FROM dbo.CategoryTranslations t WHERE t.CategoryId = v.CategoryId AND t.Locale = 'mr');

COMMIT TRANSACTION;

SELECT p.Name, p.Price, p.Mrp, t.Name AS MarathiName FROM dbo.Products p LEFT JOIN dbo.ProductTranslations t ON t.ProductId = p.Id AND t.Locale = 'mr' ORDER BY p.Name;
SELECT c.Name, t.Name AS MarathiName FROM dbo.Categories c LEFT JOIN dbo.CategoryTranslations t ON t.CategoryId = c.Id AND t.Locale = 'mr' ORDER BY c.Name;
