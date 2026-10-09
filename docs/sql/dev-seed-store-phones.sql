-- DEVELOPMENT DATABASE ONLY. Gives the dev stores a phone number so the app's "Call store" button has something to dial.
-- These are NOT real numbers: 022 49xx xxxx is made-up. Run AFTER migration AddOrderEstimateAndStorePhone is applied.
-- Safe to run twice; it only fills in stores that have no number yet.
--
--   sqlcmd -E -C -I -d retail-mobile-app -b -i docs/sql/dev-seed-store-phones.sql
SET NOCOUNT ON;

IF COL_LENGTH('dbo.Stores', 'PhoneNumber') IS NULL
    THROW 50200, 'Stores.PhoneNumber does not exist: apply migration AddOrderEstimateAndStorePhone first.', 1;

UPDATE s SET s.PhoneNumber = p.Phone
FROM dbo.Stores s
JOIN (VALUES
    (N'Harbor Point Dark Store', N'02249000001'),
    (N'Cedar Market Hub',        N'02249000002'),
    (N'North Star Fulfillment',  N'02249000003'),
    (N'Kharghar Fresh Hub',      N'02249000004'),
    (N'Belapur Dark Store',      N'02249000005'),
    (N'Panvel Market Hub',       N'02249000006')
) AS p(Name, Phone) ON p.Name = s.Name
WHERE s.PhoneNumber IS NULL;

SELECT Name, PhoneNumber FROM dbo.Stores ORDER BY Name;
