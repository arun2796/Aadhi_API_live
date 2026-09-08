-- ==============================================================================
-- Migration Script: Drop Remaining Obsolete Tables from PostgreSQL
-- Generated from migration: 20260908063700_DropRemainingObsoleteTables
-- ==============================================================================

START TRANSACTION;

-- 1. Drop foreign keys and index on Orders referencing Warehouses
ALTER TABLE IF EXISTS "Orders" DROP CONSTRAINT IF EXISTS "FK_Orders_Warehouses_WarehouseId";
DROP INDEX IF EXISTS "IX_Orders_WarehouseId";
ALTER TABLE IF EXISTS "Orders" DROP COLUMN IF EXISTS "WarehouseId";

-- 2. Drop obsolete tables in dependency order (CASCADE ensures no lingering constraints)
DROP TABLE IF EXISTS "GoodsReceiptItems" CASCADE;
DROP TABLE IF EXISTS "PurchaseOrderItems" CASCADE;
DROP TABLE IF EXISTS "Refunds" CASCADE;
DROP TABLE IF EXISTS "ReturnOrderItems" CASCADE;
DROP TABLE IF EXISTS "StockItems" CASCADE;
DROP TABLE IF EXISTS "StockMovements" CASCADE;
DROP TABLE IF EXISTS "SupplierBills" CASCADE;
DROP TABLE IF EXISTS "ReturnOrders" CASCADE;
DROP TABLE IF EXISTS "GoodsReceipts" CASCADE;
DROP TABLE IF EXISTS "PurchaseOrders" CASCADE;
DROP TABLE IF EXISTS "Suppliers" CASCADE;
DROP TABLE IF EXISTS "Warehouses" CASCADE;

-- 3. Update EF Core migrations history so EF Core knows this migration has run
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260908063700_DropRemainingObsoleteTables', '9.0.2')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;
