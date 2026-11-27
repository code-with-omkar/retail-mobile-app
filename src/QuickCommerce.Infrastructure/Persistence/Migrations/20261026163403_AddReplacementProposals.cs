using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReplacementProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LineName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    LineLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    OldQuantity = table.Column<int>(type: "int", nullable: false),
                    OldUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewVariantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NewName = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    NewLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NewQuantity = table.Column<int>(type: "int", nullable: false),
                    NewUnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NewUnitMrp = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ProposedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementProposals", x => x.Id);
                    table.CheckConstraint("CK_ReplacementProposals_Quantity", "[OldQuantity] > 0 AND [NewQuantity] > 0 AND [NewUnitPrice] >= 0");
                    table.ForeignKey(
                        name: "FK_ReplacementProposals_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementProposals_ExpiresAt",
                table: "ReplacementProposals",
                column: "ExpiresAt",
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "UX_ReplacementProposals_OnePending",
                table: "ReplacementProposals",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplacementProposals");
        }
    }
}
