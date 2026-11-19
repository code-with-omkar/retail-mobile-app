using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferCardDesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardDesign",
                table: "Campaigns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CardDesign",
                table: "Campaigns",
                sql: "[CardDesign] IS NULL OR [CardDesign] IN ('diwali','ganesh','navratri','holi','sankranti','padwa','eid','christmas')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_CardDesign",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "CardDesign",
                table: "Campaigns");
        }
    }
}
