-- Development data only: a store that delivers around Kharghar, Navi Mumbai (pincode 410209), so the app can be tried
-- from there. Additive and safe to run twice. Run against the DEVELOPMENT database, never production.
--
--   sqlcmd -E -C -d retail-mobile-app -I -i docs/sql/dev-seed-kharghar-store.sql
--
-- The point is the middle of Kharghar (approximate); the radius covers Kharghar, Taloja, Kamothe, Belapur and Panvel.
-- Move or resize it with the UPDATE at the bottom if your address is not covered.
SET NOCOUNT ON;

DECLARE @org uniqueidentifier = (SELECT TOP 1 OrganizationId FROM Stores ORDER BY Name);
DECLARE @store uniqueidentifier = (SELECT Id FROM Stores WHERE Name = N'Kharghar Fresh Hub');

IF @store IS NULL
BEGIN
    SET @store = NEWID();
    INSERT INTO Stores (Id, OrganizationId, Name, Address, Latitude, Longitude, ServiceRadiusKm, IsActive)
    VALUES (@store, @org, N'Kharghar Fresh Hub', N'Sector 20, Kharghar, Navi Mumbai 410209', 19.0420, 73.0700, 12, 1);
END

-- Every active pack the catalogue has, in stock, so the whole catalogue can be ordered from this store.
INSERT INTO StoreVariantInventory (StoreId, VariantId, AvailableQuantity, ReorderThreshold)
SELECT @store, v.Id, 50, 5
FROM ProductVariants v
WHERE v.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM StoreVariantInventory i WHERE i.StoreId = @store AND i.VariantId = v.Id);

SELECT s.Name, s.Latitude, s.Longitude, s.ServiceRadiusKm, (SELECT COUNT(*) FROM StoreVariantInventory i WHERE i.StoreId = s.Id) AS Packs
FROM Stores s WHERE s.Id = @store;

-- To move it or change how far it delivers:
-- UPDATE Stores SET Latitude = 19.0420, Longitude = 73.0700, ServiceRadiusKm = 12 WHERE Name = N'Kharghar Fresh Hub';
