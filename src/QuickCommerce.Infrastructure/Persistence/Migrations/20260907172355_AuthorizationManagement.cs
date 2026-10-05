using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthorizationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Users",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthorizationPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizationRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_AuthorizationPermissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "AuthorizationPermissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_AuthorizationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AuthorizationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AuthorizationPermissions",
                columns: new[] { "Id", "Code", "Description", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("71000000-0000-0000-0000-000000000001"), "orders:read", "Read order data", true, "View orders" },
                    { new Guid("71000000-0000-0000-0000-000000000002"), "orders:operate", "Accept and manage orders", true, "Operate on orders" },
                    { new Guid("71000000-0000-0000-0000-000000000003"), "users:manage", "Create and edit users", true, "Manage users" },
                    { new Guid("71000000-0000-0000-0000-000000000004"), "roles:manage", "Create and edit roles", true, "Manage roles" },
                    { new Guid("71000000-0000-0000-0000-000000000005"), "permissions:manage", "Create and edit permissions", true, "Manage permissions" }
                });

            migrationBuilder.InsertData(
                table: "AuthorizationRoles",
                columns: new[] { "Id", "Code", "Description", "IsActive", "Name" },
                values: new object[,]
                {
                    { new Guid("70000000-0000-0000-0000-000000000001"), "Customer", "Default customer role", true, "Customer" },
                    { new Guid("70000000-0000-0000-0000-000000000002"), "StoreStaff", "Store operations role", true, "Store Staff" },
                    { new Guid("70000000-0000-0000-0000-000000000003"), "Admin", "Platform administrator role", true, "Administrator" }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000001"),
                columns: new[] { "Email", "FirstName", "LastName" },
                values: new object[] { null, null, null });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("70000000-0000-0000-0000-000000000001") },
                    { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("70000000-0000-0000-0000-000000000002") },
                    { new Guid("71000000-0000-0000-0000-000000000002"), new Guid("70000000-0000-0000-0000-000000000002") },
                    { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("70000000-0000-0000-0000-000000000003") },
                    { new Guid("71000000-0000-0000-0000-000000000002"), new Guid("70000000-0000-0000-0000-000000000003") },
                    { new Guid("71000000-0000-0000-0000-000000000003"), new Guid("70000000-0000-0000-0000-000000000003") },
                    { new Guid("71000000-0000-0000-0000-000000000004"), new Guid("70000000-0000-0000-0000-000000000003") },
                    { new Guid("71000000-0000-0000-0000-000000000005"), new Guid("70000000-0000-0000-0000-000000000003") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationPermissions_Code",
                table: "AuthorizationPermissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationRoles_Code",
                table: "AuthorizationRoles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "AuthorizationPermissions");

            migrationBuilder.DropTable(
                name: "AuthorizationRoles");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Users");
        }
    }
}
