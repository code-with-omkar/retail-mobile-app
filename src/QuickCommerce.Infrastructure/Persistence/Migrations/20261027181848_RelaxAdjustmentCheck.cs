using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelaxAdjustmentCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderAdjustments_Refund",
                table: "OrderAdjustments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderAdjustments_Refund",
                table: "OrderAdjustments",
                sql: "[RefundPaise] >= 0 AND [NewSubtotal] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderAdjustments_Refund",
                table: "OrderAdjustments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderAdjustments_Refund",
                table: "OrderAdjustments",
                sql: "[RefundPaise] >= 0 AND [NewSubtotal] >= 0 AND [NewSubtotal] <= [OldSubtotal]");
        }
    }
}
