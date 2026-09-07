using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<double>(type: "float(9)", precision: 9, scale: 6, nullable: false),
                    ServiceRadiusKm = table.Column<double>(type: "float(9)", precision: 9, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoreInventory",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AvailableQuantity = table.Column<int>(type: "int", nullable: false),
                    ReorderThreshold = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreInventory", x => new { x.StoreId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_StoreInventory_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreInventory_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderStatusHistory_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "IsActive", "Name", "ParentCategoryId" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), true, "Groceries", null },
                    { new Guid("10000000-0000-0000-0000-000000000002"), true, "Vegetables", null },
                    { new Guid("10000000-0000-0000-0000-000000000003"), true, "Fruits", null },
                    { new Guid("10000000-0000-0000-0000-000000000004"), true, "Dairy", null }
                });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "Address", "IsActive", "Latitude", "Longitude", "Name", "ServiceRadiusKm" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), "12 Marine Drive", true, 19.076000000000001, 72.877700000000004, "Harbor Point Dark Store", 8.0 },
                    { new Guid("30000000-0000-0000-0000-000000000002"), "44 Cedar Avenue", true, 19.102, 72.915999999999997, "Cedar Market Hub", 7.0 },
                    { new Guid("30000000-0000-0000-0000-000000000003"), "8 Station Road", true, 19.045000000000002, 72.899000000000001, "North Star Fulfillment", 9.0 }
                });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "CategoryId", "Description", "ImageUrl", "IsActive", "Name", "Price", "Sku", "UnitOfMeasure" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), new Guid("10000000-0000-0000-0000-000000000002"), "Fresh red tomatoes", "https://images.unsplash.com/photo-1546094096-0df4bcaaa337?w=640", true, "Tomato", 45m, "VEG-TOM", "1 kg" },
                    { new Guid("20000000-0000-0000-0000-000000000002"), new Guid("10000000-0000-0000-0000-000000000002"), "Everyday cooking potatoes", "https://images.unsplash.com/photo-1518977676601-b53f82aba655?w=640", true, "Potato", 38m, "VEG-POT", "1 kg" },
                    { new Guid("20000000-0000-0000-0000-000000000003"), new Guid("10000000-0000-0000-0000-000000000004"), "Pasteurized full cream milk", "https://images.unsplash.com/photo-1550583724-b2692b85b150?w=640", true, "Farm Milk", 34m, "DAI-MILK", "500 ml" },
                    { new Guid("20000000-0000-0000-0000-000000000004"), new Guid("10000000-0000-0000-0000-000000000003"), "Crisp and naturally sweet", "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?w=640", true, "Royal Gala Apples", 149m, "FRT-APL", "1 kg" },
                    { new Guid("20000000-0000-0000-0000-000000000005"), new Guid("10000000-0000-0000-0000-000000000001"), "Long grain rice for every meal", "https://images.unsplash.com/photo-1586201375761-83865001e31c?w=640", true, "Daily Rice", 89m, "GRO-RICE", "1 kg" }
                });

            migrationBuilder.InsertData(
                table: "StoreInventory",
                columns: new[] { "ProductId", "StoreId", "AvailableQuantity", "ReorderThreshold" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), new Guid("30000000-0000-0000-0000-000000000001"), 24, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000002"), new Guid("30000000-0000-0000-0000-000000000001"), 8, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000003"), new Guid("30000000-0000-0000-0000-000000000001"), 0, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000004"), new Guid("30000000-0000-0000-0000-000000000001"), 16, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000005"), new Guid("30000000-0000-0000-0000-000000000001"), 5, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000001"), new Guid("30000000-0000-0000-0000-000000000002"), 28, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000002"), new Guid("30000000-0000-0000-0000-000000000002"), 12, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000003"), new Guid("30000000-0000-0000-0000-000000000002"), 4, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000004"), new Guid("30000000-0000-0000-0000-000000000002"), 20, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000005"), new Guid("30000000-0000-0000-0000-000000000002"), 9, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000001"), new Guid("30000000-0000-0000-0000-000000000003"), 32, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000002"), new Guid("30000000-0000-0000-0000-000000000003"), 16, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000003"), new Guid("30000000-0000-0000-0000-000000000003"), 8, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000004"), new Guid("30000000-0000-0000-0000-000000000003"), 24, 5 },
                    { new Guid("20000000-0000-0000-0000-000000000005"), new Guid("30000000-0000-0000-0000-000000000003"), 13, 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_CreatedAt",
                table: "Orders",
                columns: new[] { "StoreId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderStatusHistory_OrderId",
                table: "OrderStatusHistory",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_IsActive",
                table: "Products",
                columns: new[] { "CategoryId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreInventory_ProductId",
                table: "StoreInventory",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_IsActive",
                table: "Stores",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "OrderStatusHistory");

            migrationBuilder.DropTable(
                name: "StoreInventory");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
