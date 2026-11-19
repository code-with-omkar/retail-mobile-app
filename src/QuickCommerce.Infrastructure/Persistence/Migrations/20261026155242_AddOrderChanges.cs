using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PartialRefundedPaise",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "OrderAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChangesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    OldSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewSubtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundPaise = table.Column<long>(type: "bigint", nullable: false),
                    RefundStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    RefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RefundFailure = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderAdjustments", x => x.Id);
                    table.CheckConstraint("CK_OrderAdjustments_Refund", "[RefundPaise] >= 0 AND [NewSubtotal] >= 0 AND [NewSubtotal] <= [OldSubtotal]");
                    table.ForeignKey(
                        name: "FK_OrderAdjustments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderAdjustments_OrderId",
                table: "OrderAdjustments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderAdjustments_RefundStatus",
                table: "OrderAdjustments",
                column: "RefundStatus",
                filter: "[RefundStatus] IN ('Pending', 'Started')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderAdjustments");

            migrationBuilder.DropColumn(
                name: "PartialRefundedPaise",
                table: "Payments");
        }
    }
}
