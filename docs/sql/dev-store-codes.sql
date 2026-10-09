-- Dev only: friendlier store codes for the order numbers (STORE-yyMMdd-nnnn). Safe to run more than once.
-- The migration AddStoreCodesAndOrderNumberCounters gives every store a code from its name; this sets the ones we prefer.
UPDATE Stores SET Code = 'KHG' WHERE Name = 'Kharghar Fresh Hub' AND NOT EXISTS (SELECT 1 FROM Stores o WHERE o.Code = 'KHG' AND o.Name <> 'Kharghar Fresh Hub');
UPDATE Stores SET Code = 'PNV' WHERE Name = 'Panvel Market Hub' AND NOT EXISTS (SELECT 1 FROM Stores o WHERE o.Code = 'PNV' AND o.Name <> 'Panvel Market Hub');
UPDATE Stores SET Code = 'HBR' WHERE Name = 'Harbor Point Dark Store' AND NOT EXISTS (SELECT 1 FROM Stores o WHERE o.Code = 'HBR' AND o.Name <> 'Harbor Point Dark Store');
UPDATE Stores SET Code = 'BLP' WHERE Name = 'Belapur Dark Store' AND NOT EXISTS (SELECT 1 FROM Stores o WHERE o.Code = 'BLP' AND o.Name <> 'Belapur Dark Store');
UPDATE Stores SET Code = 'CDR' WHERE Name = 'Cedar Market Hub' AND NOT EXISTS (SELECT 1 FROM Stores o WHERE o.Code = 'CDR' AND o.Name <> 'Cedar Market Hub');
UPDATE Stores SET Code = 'NSF' WHERE Name = 'North Star Fulfillment' AND NOT EXISTS (SELECT 1 FROM Stores o WHERE o.Code = 'NSF' AND o.Name <> 'North Star Fulfillment');
SELECT Name, Code FROM Stores ORDER BY Name;
