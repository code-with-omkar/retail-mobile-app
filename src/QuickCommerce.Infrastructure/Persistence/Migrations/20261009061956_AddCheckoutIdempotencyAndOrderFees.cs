using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutIdempotencyAndOrderFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryAddressId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryFee",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HandlingFee",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CashOnDelivery");

            migrationBuilder.AddColumn<string>(
                name: "ReceiverName",
                table: "Orders",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiverPhone",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SubtotalAmount",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Orders placed before fees existed: what was paid is the products, with no fees.
            migrationBuilder.Sql("UPDATE [Orders] SET [SubtotalAmount] = [TotalAmount];");

            migrationBuilder.CreateTable(
                name: "CheckoutRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckoutRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CheckoutRequests_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CheckoutRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Fees",
                table: "Orders",
                sql: "[DeliveryFee] >= 0 AND [HandlingFee] >= 0 AND [SubtotalAmount] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutRequests_CreatedAt",
                table: "CheckoutRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CheckoutRequests_OrderId",
                table: "CheckoutRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "UX_CheckoutRequests_CustomerKey",
                table: "CheckoutRequests",
                columns: new[] { "CustomerId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckoutRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Fees",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliveryFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HandlingFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReceiverName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReceiverPhone",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SubtotalAmount",
                table: "Orders");
        }
    }
}
