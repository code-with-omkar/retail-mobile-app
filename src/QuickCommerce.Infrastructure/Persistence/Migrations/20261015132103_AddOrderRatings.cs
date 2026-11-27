using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderRatings",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeliveryPartnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StoreStars = table.Column<int>(type: "int", nullable: false),
                    DeliveryStars = table.Column<int>(type: "int", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRatings", x => x.OrderId);
                    table.CheckConstraint("CK_OrderRatings_DeliveryStars", "[DeliveryStars] IS NULL OR [DeliveryStars] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_OrderRatings_StoreStars", "[StoreStars] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_OrderRatings_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRatings_DeliveryPartnerId_CreatedAt",
                table: "OrderRatings",
                columns: new[] { "DeliveryPartnerId", "CreatedAt" },
                filter: "[DeliveryPartnerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRatings_StoreId_CreatedAt",
                table: "OrderRatings",
                columns: new[] { "StoreId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRatings");
        }
    }
}
