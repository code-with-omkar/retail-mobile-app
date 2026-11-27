using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Animation",
                table: "Coupons",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Design",
                table: "Coupons",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Coupons",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ribbon",
                table: "Coupons",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShowAsPopup",
                table: "Coupons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShowInApp",
                table: "Coupons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Animation",
                table: "Campaigns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ribbon",
                table: "Campaigns",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Animation",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "Design",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "Ribbon",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "ShowAsPopup",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "ShowInApp",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "Animation",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "Ribbon",
                table: "Campaigns");
        }
    }
}
