-- DEVELOPMENT DATABASE ONLY. Never run against a shared, staging or production database.
--
-- Gives the dev catalogue enough to browse: a Snacks category, about 30 more products (each with a default 1-unit pack, an MRP where
-- it has a discount, and a Marathi name and description), 500 g / 250 g packs for a few loose items, and two more stores around
-- Kharghar so the store list has a choice. Every active pack is stocked in every store that does not carry it yet.
-- Idempotent: running it twice changes nothing the second time. Run AFTER dev-seed-kharghar-store.sql.
--
--   sqlcmd -S localhost -E -C -I -f 65001 -d retail-mobile-app -b -i docs/sql/dev-seed-catalogue-and-stores.sql
--
-- (-I is needed for the filtered unique index on ProductVariants; -f 65001 reads this file as UTF-8 so the Marathi text is kept.)
-- Product photos are left empty on purpose: the app shows a category icon until real photos are uploaded (task 1.5).

SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;

DECLARE @groceries uniqueidentifier = '10000000-0000-0000-0000-000000000001';
DECLARE @veg       uniqueidentifier = '10000000-0000-0000-0000-000000000002';
DECLARE @fruits    uniqueidentifier = '10000000-0000-0000-0000-000000000003';
DECLARE @dairy     uniqueidentifier = '10000000-0000-0000-0000-000000000004';
DECLARE @snacks    uniqueidentifier = '10000000-0000-0000-0000-000000000005';

-- ---------- category ----------
IF NOT EXISTS (SELECT 1 FROM dbo.Categories WHERE Id = @snacks)
    INSERT INTO dbo.Categories (Id, Name, ParentCategoryId, IsActive) VALUES (@snacks, N'Snacks', NULL, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.CategoryTranslations WHERE CategoryId = @snacks AND Locale = N'mr')
    INSERT INTO dbo.CategoryTranslations (CategoryId, Locale, Name) VALUES (@snacks, N'mr', N'स्नॅक्स');

-- ---------- products ----------
DECLARE @p TABLE (Sku nvarchar(40), Name nvarchar(200), Descr nvarchar(500), CategoryId uniqueidentifier, Price decimal(18,2), Mrp decimal(18,2) NULL,
                  Unit nvarchar(40), MrName nvarchar(200), MrDescr nvarchar(500), LoosePacks bit);
INSERT @p VALUES
 (N'VEG-ONI',  N'Onion',            N'Firm red onions for everyday cooking',        @veg,   34, 40,   N'1 kg',   N'कांदा',            N'रोजच्या स्वयंपाकासाठी घट्ट लाल कांदे', 1),
 (N'VEG-CAR',  N'Carrots',          N'Sweet, crunchy orange carrots',               @veg,   38, NULL, N'500 g',  N'गाजर',             N'गोड आणि कुरकुरीत गाजर', 0),
 (N'VEG-SPI',  N'Spinach',          N'Fresh leafy spinach, washed',                 @veg,   20, NULL, N'250 g',  N'पालक',             N'ताजा धुतलेला पालक', 0),
 (N'VEG-CUC',  N'Cucumber',         N'Cool, crisp cucumbers',                       @veg,   30, NULL, N'500 g',  N'काकडी',            N'थंडगार कुरकुरीत काकडी', 0),
 (N'VEG-CAP',  N'Green Capsicum',   N'Glossy green capsicum',                       @veg,   28, 32,   N'250 g',  N'ढोबळी मिरची',      N'हिरवी ताजी ढोबळी मिरची', 0),
 (N'VEG-CAU',  N'Cauliflower',      N'Fresh white cauliflower',                     @veg,   40, NULL, N'1 pc',   N'फ्लॉवर',           N'ताजा पांढरा फ्लॉवर', 0),
 (N'VEG-PEA',  N'Green Peas',       N'Tender green peas in the pod',                @veg,   60, 70,   N'500 g',  N'हिरवे मटार',       N'कोवळे हिरवे मटार', 0),
 (N'VEG-COR',  N'Coriander',        N'Fragrant fresh coriander leaves',             @veg,   15, NULL, N'100 g',  N'कोथिंबीर',         N'सुगंधी ताजी कोथिंबीर', 0),
 (N'VEG-LAD',  N'Ladyfinger',       N'Tender bhindi, hand picked',                  @veg,   35, NULL, N'500 g',  N'भेंडी',            N'कोवळी निवडक भेंडी', 0),
 (N'FRT-BAN',  N'Bananas',          N'Ripe yellow bananas',                         @fruits,42, 48,   N'6 pcs',  N'केळी',             N'पिकलेली पिवळी केळी', 0),
 (N'FRT-POM',  N'Pomegranate',      N'Ruby red, juicy pomegranate',                 @fruits,160,190,  N'1 kg',   N'डाळिंब',           N'लालबुंद रसाळ डाळिंब', 1),
 (N'FRT-ORA',  N'Oranges',          N'Sweet and tangy nagpur oranges',              @fruits,90, 105,  N'1 kg',   N'संत्री',           N'गोड आंबट नागपुरी संत्री', 1),
 (N'FRT-GRA',  N'Green Grapes',     N'Seedless green grapes',                       @fruits,70, 80,   N'500 g',  N'हिरवी द्राक्षे',   N'बिया नसलेली हिरवी द्राक्षे', 0),
 (N'FRT-PAP',  N'Papaya',           N'Ripe, sweet papaya',                          @fruits,55, NULL, N'1 pc',   N'पपई',              N'पिकलेली गोड पपई', 0),
 (N'FRT-WAT',  N'Watermelon',       N'Juicy red watermelon',                        @fruits,80, NULL, N'1 pc',   N'कलिंगड',           N'रसाळ लाल कलिंगड', 0),
 (N'DAI-CUR',  N'Fresh Curd',       N'Thick set curd made daily',                   @dairy, 40, NULL, N'400 g',  N'ताजे दही',         N'रोज बनवलेले घट्ट दही', 0),
 (N'DAI-PAN',  N'Paneer',           N'Soft fresh paneer block',                     @dairy, 85, 95,   N'200 g',  N'पनीर',             N'मऊ ताजा पनीर', 0),
 (N'DAI-BUT',  N'Butter',           N'Creamy salted butter',                        @dairy, 58, 60,   N'100 g',  N'लोणी',             N'मलईदार खारट लोणी', 0),
 (N'DAI-EGG',  N'Farm Eggs',        N'Fresh farm eggs',                             @dairy, 54, 60,   N'6 pcs',  N'अंडी',             N'ताजी फार्मची अंडी', 0),
 (N'DAI-CHE',  N'Cheese Slices',    N'Processed cheese slices',                     @dairy, 120,135,  N'200 g',  N'चीज स्लाइस',       N'प्रोसेस्ड चीज स्लाइस', 0),
 (N'GRO-ATT',  N'Whole Wheat Atta', N'Stone-ground whole wheat flour',              @groceries,245,270,N'5 kg',  N'गव्हाचे पीठ',      N'दळलेले संपूर्ण गव्हाचे पीठ', 0),
 (N'GRO-TOO',  N'Toor Dal',         N'Unpolished toor dal',                         @groceries,165,180,N'1 kg',  N'तूर डाळ',          N'पॉलिश न केलेली तूर डाळ', 0),
 (N'GRO-SUG',  N'Sugar',            N'Fine white sugar',                            @groceries,48, NULL,N'1 kg', N'साखर',             N'बारीक पांढरी साखर', 0),
 (N'GRO-OIL',  N'Sunflower Oil',    N'Refined sunflower cooking oil',               @groceries,150,165,N'1 l',   N'सूर्यफूल तेल',     N'शुद्ध सूर्यफूल स्वयंपाकाचे तेल', 0),
 (N'GRO-TEA',  N'Tea Leaves',       N'Strong Assam tea leaves',                     @groceries,120,130,N'250 g', N'चहा पावडर',        N'कडक आसाम चहा पावडर', 0),
 (N'GRO-SAL',  N'Iodised Salt',     N'Free-flowing iodised salt',                   @groceries,22, NULL,N'1 kg', N'आयोडीनयुक्त मीठ',  N'मोकळे आयोडीनयुक्त मीठ', 0),
 (N'SNK-COO',  N'Butter Cookies',   N'Crumbly butter cookies',                      @snacks,60, 75,   N'200 g',  N'बटर कुकीज',        N'कुरकुरीत बटर कुकीज', 0),
 (N'SNK-CHI',  N'Potato Chips',     N'Classic salted potato chips',                 @snacks,40, 50,   N'150 g',  N'बटाट्याचे वेफर्स', N'खारट बटाट्याचे वेफर्स', 0),
 (N'SNK-BRE',  N'Sandwich Bread',   N'Soft sliced sandwich bread',                  @snacks,45, NULL, N'400 g',  N'ब्रेड',            N'मऊ स्लाइस सँडविच ब्रेड', 0),
 (N'SNK-NAM',  N'Namkeen Mix',      N'Crunchy spicy namkeen mix',                   @snacks,55, 65,   N'200 g',  N'चिवडा',            N'कुरकुरीत मसालेदार चिवडा', 0);

INSERT INTO dbo.Products (Id, Sku, Name, Description, Price, Mrp, UnitOfMeasure, CategoryId, ImageUrl, IsActive)
SELECT NEWID(), p.Sku, p.Name, p.Descr, p.Price, p.Mrp, p.Unit, p.CategoryId, NULL, 1
FROM @p p WHERE NOT EXISTS (SELECT 1 FROM dbo.Products x WHERE x.Sku = p.Sku);

INSERT INTO dbo.ProductTranslations (ProductId, Locale, Name, Description)
SELECT x.Id, N'mr', p.MrName, p.MrDescr
FROM @p p JOIN dbo.Products x ON x.Sku = p.Sku
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductTranslations t WHERE t.ProductId = x.Id AND t.Locale = N'mr');

-- ---------- packs ----------
-- Every new product gets its default pack (the unit shown on the card).
INSERT INTO dbo.ProductVariants (Id, ProductId, Sku, Label, Price, Mrp, SortOrder, IsDefault, IsActive)
SELECT NEWID(), x.Id, x.Sku, p.Unit, p.Price, p.Mrp, 2, 1, 1
FROM @p p JOIN dbo.Products x ON x.Sku = p.Sku
WHERE NOT EXISTS (SELECT 1 FROM dbo.ProductVariants v WHERE v.ProductId = x.Id AND v.IsDefault = 1);

-- Loose items also come in 500 g and 250 g (a little dearer per kg, like the earlier seed).
INSERT INTO dbo.ProductVariants (Id, ProductId, Sku, Label, Price, Mrp, SortOrder, IsDefault, IsActive)
SELECT NEWID(), x.Id, LEFT(x.Sku + k.Suffix, 80), k.Label, CEILING(p.Price * k.Factor), CASE WHEN p.Mrp IS NULL THEN NULL ELSE CEILING(p.Mrp * k.Factor) END, k.SortOrder, 0, 1
FROM @p p
JOIN dbo.Products x ON x.Sku = p.Sku
CROSS JOIN (VALUES (N'-500G', N'500 g', CAST(0.52 AS decimal(5,2)), 1), (N'-250G', N'250 g', CAST(0.28 AS decimal(5,2)), 0)) AS k(Suffix, Label, Factor, SortOrder)
WHERE p.LoosePacks = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductVariants v WHERE v.ProductId = x.Id AND v.Label = k.Label);

-- ---------- two more stores around Kharghar ----------
DECLARE @org uniqueidentifier = (SELECT TOP 1 OrganizationId FROM dbo.Stores ORDER BY Name);
IF NOT EXISTS (SELECT 1 FROM dbo.Stores WHERE Name = N'Belapur Dark Store')
    INSERT INTO dbo.Stores (Id, OrganizationId, Name, Address, Latitude, Longitude, ServiceRadiusKm, IsActive)
    VALUES (NEWID(), @org, N'Belapur Dark Store', N'Sector 11, CBD Belapur, Navi Mumbai 400614', 19.0190, 73.0400, 9, 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Stores WHERE Name = N'Panvel Market Hub')
    INSERT INTO dbo.Stores (Id, OrganizationId, Name, Address, Latitude, Longitude, ServiceRadiusKm, IsActive)
    VALUES (NEWID(), @org, N'Panvel Market Hub', N'Old Panvel Market Road, Panvel 410206', 18.9894, 73.1175, 8, 1);

-- ---------- stock: every active pack in every store that does not carry it yet ----------
INSERT INTO dbo.StoreVariantInventory (StoreId, VariantId, AvailableQuantity, ReorderThreshold)
SELECT s.Id, v.Id, 40, 5
FROM dbo.Stores s
CROSS JOIN dbo.ProductVariants v
WHERE s.IsActive = 1 AND v.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.StoreVariantInventory i WHERE i.StoreId = s.Id AND i.VariantId = v.Id);

COMMIT;

SELECT c.Name AS Category, COUNT(*) AS Products
FROM dbo.Products p JOIN dbo.Categories c ON c.Id = p.CategoryId WHERE p.IsActive = 1 GROUP BY c.Name ORDER BY c.Name;
SELECT s.Name, s.ServiceRadiusKm, (SELECT COUNT(*) FROM dbo.StoreVariantInventory i WHERE i.StoreId = s.Id) AS Packs FROM dbo.Stores s ORDER BY s.Name;
