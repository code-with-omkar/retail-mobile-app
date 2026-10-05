using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DummyUserSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "DisplayName", "Email", "ExternalSubject", "FirstName", "IsActive", "LastName", "OrganizationId", "Role", "StoreId" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000002"), "Demo Administrator", "admin@example.test", "admin", "Demo", true, "Administrator", new Guid("00000000-0000-0000-0000-000000000001"), "Admin", null },
                    { new Guid("40000000-0000-0000-0000-000000000003"), "Demo Store Staff", "storestaff@example.test", "storestaff", "Demo", true, "Store Staff", new Guid("00000000-0000-0000-0000-000000000001"), "StoreStaff", new Guid("30000000-0000-0000-0000-000000000001") },
                    { new Guid("40000000-0000-0000-0000-000000000004"), "Demo Customer", "customer@example.test", "customer", "Demo", true, "Customer", new Guid("00000000-0000-0000-0000-000000000001"), "Customer", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000004"));
        }
    }
}
