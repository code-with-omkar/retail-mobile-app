using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliverySlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliverySlotEnd",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeliverySlotStart",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeliverySlotSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    OpenMinutes = table.Column<int>(type: "int", nullable: false),
                    CloseMinutes = table.Column<int>(type: "int", nullable: false),
                    WindowMinutes = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    CutoffMinutes = table.Column<int>(type: "int", nullable: false),
                    DaysAhead = table.Column<int>(type: "int", nullable: false),
                    AsapAllowed = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySlotSettings", x => x.Id);
                    table.CheckConstraint("CK_DeliverySlotSettings_Ranges", "[OpenMinutes] >= 0 AND [CloseMinutes] <= 1440 AND [CloseMinutes] > [OpenMinutes] AND [WindowMinutes] > 0 AND [Capacity] >= 1 AND [CutoffMinutes] >= 0 AND [DaysAhead] BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_DeliverySlotSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SlotBookings",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Booked = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlotBookings", x => new { x.StoreId, x.SlotStart });
                    table.CheckConstraint("CK_SlotBookings_Booked", "[Booked] >= 0");
                    table.ForeignKey(
                        name: "FK_SlotBookings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreSlot",
                table: "Orders",
                columns: new[] { "StoreId", "DeliverySlotStart" },
                filter: "[DeliverySlotStart] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Slot",
                table: "Orders",
                sql: "([DeliverySlotStart] IS NULL AND [DeliverySlotEnd] IS NULL) OR ([DeliverySlotStart] IS NOT NULL AND [DeliverySlotEnd] > [DeliverySlotStart])");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySlotSettings_StoreId",
                table: "DeliverySlotSettings",
                column: "StoreId",
                unique: true,
                filter: "[StoreId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliverySlotSettings");

            migrationBuilder.DropTable(
                name: "SlotBookings");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreSlot",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Slot",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliverySlotEnd",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DeliverySlotStart",
                table: "Orders");
        }
    }
}
