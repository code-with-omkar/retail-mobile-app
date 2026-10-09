using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Notifications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Order");

            migrationBuilder.AddColumn<string>(
                name: "DataJson",
                table: "Notifications",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OffersEnabled",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    BodyEn = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TitleMr = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    BodyMr = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                    table.CheckConstraint("CK_Campaigns_RecipientCount", "[RecipientCount] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CampaignId_CustomerId",
                table: "Notifications",
                columns: new[] { "CampaignId", "CustomerId" },
                unique: true,
                filter: "[CampaignId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_StartsAt",
                table: "Campaigns",
                column: "StartsAt",
                filter: "[PublishedAt] IS NULL AND [IsActive] = 1");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Campaigns_CampaignId",
                table: "Notifications",
                column: "CampaignId",
                principalTable: "Campaigns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Earlier payment notifications get their category (earlier order ones keep the column default, Order).
            migrationBuilder.Sql("UPDATE Notifications SET Category = 'Payment' WHERE Type = 'PaymentUpdate';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Campaigns_CampaignId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_CampaignId_CustomerId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CampaignId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "DataJson",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "OffersEnabled",
                table: "Customers");
        }
    }
}
