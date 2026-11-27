using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenOfferCardDesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_CardDesign",
                table: "Campaigns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CardDesign",
                table: "Campaigns",
                sql: "[CardDesign] IS NULL OR [CardDesign] IN ('diwali','ganesh','navratri','holi','sankranti','padwa','eid','christmas','fresh','weekend','bigsaver','combo','freeship','newin')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Campaigns_CardDesign",
                table: "Campaigns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Campaigns_CardDesign",
                table: "Campaigns",
                sql: "[CardDesign] IS NULL OR [CardDesign] IN ('diwali','ganesh','navratri','holi','sankranti','padwa','eid','christmas')");
        }
    }
}
