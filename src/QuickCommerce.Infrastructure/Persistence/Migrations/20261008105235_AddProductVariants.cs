using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase P3: product variants (pack sizes). Additive and ordered so that existing data is never lost:
    /// 1. create ProductVariants and StoreVariantInventory,
    /// 2. backfill one default variant per product and one variant-stock row per existing stock row,
    /// 3. verify the backfill and fail loudly if it is incomplete,
    /// 4. add the variant columns to cart and order lines and re-key the cart by variant.
    /// The old StoreInventory table is left untouched as the rollback copy. Nothing is dropped.
    /// </summary>
    /// <inheritdoc />
    public partial class AddProductVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. new tables ----
            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Mrp = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.CheckConstraint("CK_ProductVariants_MrpAtLeastPrice", "[Mrp] IS NULL OR [Mrp] >= [Price]");
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId_SortOrder",
                table: "ProductVariants",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Sku",
                table: "ProductVariants",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariants_OneDefaultPerProduct",
                table: "ProductVariants",
                column: "ProductId",
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateTable(
                name: "StoreVariantInventory",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AvailableQuantity = table.Column<int>(type: "int", nullable: false),
                    ReorderThreshold = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreVariantInventory", x => new { x.StoreId, x.VariantId });
                    table.CheckConstraint("CK_StoreVariantInventory_NotNegative", "[AvailableQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_StoreVariantInventory_ProductVariants_VariantId",
                        column: x => x.VariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreVariantInventory_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoreVariantInventory_VariantId",
                table: "StoreVariantInventory",
                column: "VariantId");

            // ---- 2. backfill (idempotent: safe to run again) ----
            // One default variant per product, copying its price, MRP, unit (as the label) and SKU. The product row is not touched.
            migrationBuilder.Sql(@"
INSERT INTO [ProductVariants] ([Id], [ProductId], [Sku], [Label], [Price], [Mrp], [SortOrder], [IsDefault], [IsActive])
SELECT NEWID(), p.[Id], p.[Sku], LEFT(p.[UnitOfMeasure], 40), p.[Price], p.[Mrp], 0, 1, 1
FROM [Products] p
WHERE NOT EXISTS (SELECT 1 FROM [ProductVariants] v WHERE v.[ProductId] = p.[Id] AND v.[IsDefault] = 1);");

            // One variant-stock row per existing stock row, on that product's default variant.
            migrationBuilder.Sql(@"
INSERT INTO [StoreVariantInventory] ([StoreId], [VariantId], [AvailableQuantity], [ReorderThreshold])
SELECT si.[StoreId], v.[Id], si.[AvailableQuantity], si.[ReorderThreshold]
FROM [StoreInventory] si
JOIN [ProductVariants] v ON v.[ProductId] = si.[ProductId] AND v.[IsDefault] = 1
WHERE NOT EXISTS (SELECT 1 FROM [StoreVariantInventory] x WHERE x.[StoreId] = si.[StoreId] AND x.[VariantId] = v.[Id]);");

            // ---- 3. verify: fail loudly rather than carry on with a partial backfill ----
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Products] p WHERE (SELECT COUNT(*) FROM [ProductVariants] v WHERE v.[ProductId] = p.[Id] AND v.[IsDefault] = 1) <> 1)
    THROW 50001, 'P3 backfill failed: a product does not have exactly one default variant.', 1;
IF EXISTS (SELECT 1 FROM [StoreInventory] si WHERE NOT EXISTS (
        SELECT 1 FROM [StoreVariantInventory] x JOIN [ProductVariants] v ON v.[Id] = x.[VariantId] AND v.[IsDefault] = 1
        WHERE x.[StoreId] = si.[StoreId] AND v.[ProductId] = si.[ProductId]))
    THROW 50002, 'P3 backfill failed: a stock row was not copied to variant stock.', 1;");

            // ---- 4. order lines: nullable snapshots; old orders keep null ----
            migrationBuilder.AddColumn<decimal>(
                name: "UnitMrpSnapshot",
                table: "OrderItems",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "OrderItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantLabelSnapshot",
                table: "OrderItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_VariantId",
                table: "OrderItems",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_ProductVariants_VariantId",
                table: "OrderItems",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---- 5. cart lines: add the columns, point existing lines at the default variant, then re-key by variant ----
            migrationBuilder.AddColumn<Guid>(
                name: "VariantId",
                table: "CartItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "VariantLabelSnapshot",
                table: "CartItems",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
UPDATE ci SET ci.[VariantId] = v.[Id], ci.[VariantLabelSnapshot] = v.[Label]
FROM [CartItems] ci
JOIN [ProductVariants] v ON v.[ProductId] = ci.[ProductId] AND v.[IsDefault] = 1;");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CartItems",
                table: "CartItems");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CartItems",
                table: "CartItems",
                columns: new[] { "CartId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_VariantId",
                table: "CartItems",
                column: "VariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_ProductVariants_VariantId",
                table: "CartItems",
                column: "VariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Stock goes back to the old table first so a rollback does not lose sales made while variants were live.
            // Only default-variant stock can be represented there; other pack sizes have no home in the old model.
            migrationBuilder.Sql(@"
UPDATE si SET si.[AvailableQuantity] = x.[AvailableQuantity]
FROM [StoreInventory] si
JOIN [ProductVariants] v ON v.[ProductId] = si.[ProductId] AND v.[IsDefault] = 1
JOIN [StoreVariantInventory] x ON x.[StoreId] = si.[StoreId] AND x.[VariantId] = v.[Id];");

            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_ProductVariants_VariantId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_ProductVariants_VariantId",
                table: "OrderItems");

            // The old cart key is (cart, product), so two pack sizes of one product cannot both stay: keep the earliest line.
            // Carts are short-lived data; order lines are never touched.
            migrationBuilder.Sql(@"
WITH numbered AS (
    SELECT ROW_NUMBER() OVER (PARTITION BY [CartId], [ProductId] ORDER BY [AddedAt], [VariantId]) AS rn FROM [CartItems])
DELETE FROM numbered WHERE rn > 1;");

            migrationBuilder.DropPrimaryKey(
                name: "PK_CartItems",
                table: "CartItems");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_VariantId",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "VariantLabelSnapshot",
                table: "CartItems");

            migrationBuilder.AddPrimaryKey(
                name: "PK_CartItems",
                table: "CartItems",
                columns: new[] { "CartId", "ProductId" });

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_VariantId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "UnitMrpSnapshot",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "VariantLabelSnapshot",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "StoreVariantInventory");

            migrationBuilder.DropTable(
                name: "ProductVariants");
        }
    }
}
