using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidPlacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlacementBids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Pincode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    WeekStart = table.Column<DateOnly>(type: "date", nullable: false),
                    AmountPaise = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlacementBids", x => x.Id);
                    table.CheckConstraint("CK_PlacementBids_Amount", "[AmountPaise] > 0 AND [Attempts] >= 0");
                    table.ForeignKey(
                        name: "FK_PlacementBids_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlacementRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    MinBidPaise = table.Column<long>(type: "bigint", nullable: false),
                    MinStepPaise = table.Column<long>(type: "bigint", nullable: false),
                    MaxWeeksAhead = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlacementRules", x => x.Id);
                    table.CheckConstraint("CK_PlacementRules_Values", "[MinBidPaise] > 0 AND [MinStepPaise] >= 0 AND [MaxWeeksAhead] BETWEEN 1 AND 26");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlacementBids_PaymentStatus_UpdatedAt",
                table: "PlacementBids",
                columns: new[] { "PaymentStatus", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlacementBids_StoreId_WeekStart",
                table: "PlacementBids",
                columns: new[] { "StoreId", "WeekStart" });

            migrationBuilder.CreateIndex(
                name: "UX_PlacementBids_Leading",
                table: "PlacementBids",
                columns: new[] { "Pincode", "WeekStart" },
                unique: true,
                filter: "[Status] = 'Leading'");

            migrationBuilder.CreateIndex(
                name: "UX_PlacementBids_ProviderOrderId",
                table: "PlacementBids",
                column: "ProviderOrderId",
                unique: true,
                filter: "[ProviderOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PlacementBids_ProviderPaymentId",
                table: "PlacementBids",
                column: "ProviderPaymentId",
                unique: true,
                filter: "[ProviderPaymentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlacementBids");

            migrationBuilder.DropTable(
                name: "PlacementRules");
        }
    }
}
