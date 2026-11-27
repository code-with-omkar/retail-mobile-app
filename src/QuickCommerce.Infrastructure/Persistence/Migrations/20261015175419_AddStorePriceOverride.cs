using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStorePriceOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PriceOverride",
                table: "StoreVariantInventory",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StoreVariantInventory_PriceOverride",
                table: "StoreVariantInventory",
                sql: "[PriceOverride] IS NULL OR [PriceOverride] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StoreVariantInventory_PriceOverride",
                table: "StoreVariantInventory");

            migrationBuilder.DropColumn(
                name: "PriceOverride",
                table: "StoreVariantInventory");
        }
    }
}
