using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreCodesAndOrderNumberCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Stores",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderNumberCounters",
                columns: table => new
                {
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNumberCounters", x => new { x.StoreId, x.Day });
                    table.CheckConstraint("CK_OrderNumberCounters_LastNumber", "[LastNumber] >= 0");
                    table.ForeignKey(
                        name: "FK_OrderNumberCounters_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000001"),
                column: "Code",
                value: null);

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000002"),
                column: "Code",
                value: null);

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000003"),
                column: "Code",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_Stores_Code",
                table: "Stores",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            // Existing stores get a code from their name (first three letters), made unique with a number when two share the same letters.
            // Admins can change the codes later; orders already placed keep their old numbers.
            migrationBuilder.Sql(@"
WITH named AS (
    SELECT Id, UPPER(LEFT(REPLACE(REPLACE(REPLACE(Name, ' ', ''), '-', ''), '''', ''), 3)) AS Base
    FROM Stores WHERE Code IS NULL),
numbered AS (
    SELECT Id, Base, ROW_NUMBER() OVER (PARTITION BY Base ORDER BY Id) AS Rn FROM named)
UPDATE s SET Code = CASE WHEN n.Rn = 1 THEN n.Base ELSE LEFT(n.Base, 2) + CAST(n.Rn AS varchar(2)) END
FROM Stores s JOIN numbered n ON n.Id = s.Id
WHERE n.Base <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderNumberCounters");

            migrationBuilder.DropIndex(
                name: "IX_Stores_Code",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Stores");
        }
    }
}
