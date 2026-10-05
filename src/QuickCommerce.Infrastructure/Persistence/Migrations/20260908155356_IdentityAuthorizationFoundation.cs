using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityAuthorizationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Users",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Users",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "RolePermissions",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Organizations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Organizations",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "Organizations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "Organizations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AuthorizationRoles",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "AuthorizationRoles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "AuthorizationRoles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "AuthorizationRoles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "AuthorizationPermissions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "AuthorizationPermissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "AuthorizationPermissions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "AuthorizationPermissions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_AuthorizationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AuthorizationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStoreAssignments",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStoreAssignments", x => new { x.UserId, x.StoreId });
                    table.ForeignKey(
                        name: "FK_UserStoreAssignments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserStoreAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000002"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000003"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000004"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000005"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.InsertData(
                table: "AuthorizationPermissions",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "Description", "IsActive", "Name", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("71000000-0000-0000-0000-000000000006"), "Store.Read", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Read store data", true, "Read stores", null, null },
                    { new Guid("71000000-0000-0000-0000-000000000007"), "Customer.Read", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Read customer data", true, "Read customers", null, null },
                    { new Guid("71000000-0000-0000-0000-000000000008"), "Delivery.Read", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Read future delivery data", true, "Read deliveries", null, null }
                });

            migrationBuilder.UpdateData(
                table: "AuthorizationRoles",
                keyColumn: "Id",
                keyValue: new Guid("70000000-0000-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "AuthorizationRoles",
                keyColumn: "Id",
                keyValue: new Guid("70000000-0000-0000-0000-000000000002"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "AuthorizationRoles",
                keyColumn: "Id",
                keyValue: new Guid("70000000-0000-0000-0000-000000000003"),
                columns: new[] { "Code", "CreatedAt", "CreatedBy", "Description", "Name", "UpdatedAt", "UpdatedBy" },
                values: new object[] { "ApplicationAdmin", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Organization-wide administration role", "Application Admin", null, null });

            migrationBuilder.InsertData(
                table: "AuthorizationRoles",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "Description", "IsActive", "Name", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("70000000-0000-0000-0000-000000000004"), "DeliveryPartner", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Future delivery operations role", true, "Delivery Partner", null, null });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "Id", "IsActive", "UserId" },
                values: new object[] { new Guid("50000000-0000-0000-0000-000000000002"), true, new Guid("40000000-0000-0000-0000-000000000004") });

            migrationBuilder.UpdateData(
                table: "Organizations",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                columns: new[] { "Code", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { "QUICKCART-DEMO", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("70000000-0000-0000-0000-000000000001") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("70000000-0000-0000-0000-000000000002") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000002"), new Guid("70000000-0000-0000-0000-000000000002") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000001"), new Guid("70000000-0000-0000-0000-000000000003") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000002"), new Guid("70000000-0000-0000-0000-000000000003") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000003"), new Guid("70000000-0000-0000-0000-000000000003") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000004"), new Guid("70000000-0000-0000-0000-000000000003") },
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000005"), new Guid("70000000-0000-0000-0000-000000000003") },
                column: "IsActive",
                value: true);

            migrationBuilder.InsertData(
                table: "UserRoles",
                columns: new[] { "RoleId", "UserId", "CreatedAt", "CreatedBy", "IsActive", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("70000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000001"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null },
                    { new Guid("70000000-0000-0000-0000-000000000003"), new Guid("40000000-0000-0000-0000-000000000002"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null },
                    { new Guid("70000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000003"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null },
                    { new Guid("70000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000004"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null }
                });

            migrationBuilder.InsertData(
                table: "UserStoreAssignments",
                columns: new[] { "StoreId", "UserId", "CreatedAt", "CreatedBy", "EffectiveFrom", "EffectiveTo", "IsActive", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000003"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000001"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000002"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000003"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000004"),
                columns: new[] { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" },
                values: new object[] { new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", null, null });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DisplayName", "Email", "ExternalSubject", "FirstName", "IsActive", "LastName", "OrganizationId", "Role", "StoreId", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000005"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Demo Multi Store Staff", "multistorestaff@example.test", "multistorestaff", "Demo", true, "Multi Store Staff", new Guid("00000000-0000-0000-0000-000000000001"), "StoreStaff", new Guid("30000000-0000-0000-0000-000000000001"), null, null },
                    { new Guid("40000000-0000-0000-0000-000000000006"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Demo Delivery Partner", "deliverypartner@example.test", "deliverypartner", "Demo", true, "Delivery Partner", new Guid("00000000-0000-0000-0000-000000000001"), "DeliveryPartner", null, null, null }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId", "IsActive" },
                values: new object[,]
                {
                    { new Guid("71000000-0000-0000-0000-000000000006"), new Guid("70000000-0000-0000-0000-000000000003"), true },
                    { new Guid("71000000-0000-0000-0000-000000000007"), new Guid("70000000-0000-0000-0000-000000000003"), true },
                    { new Guid("71000000-0000-0000-0000-000000000008"), new Guid("70000000-0000-0000-0000-000000000003"), true },
                    { new Guid("71000000-0000-0000-0000-000000000008"), new Guid("70000000-0000-0000-0000-000000000004"), true }
                });

            migrationBuilder.InsertData(
                table: "UserRoles",
                columns: new[] { "RoleId", "UserId", "CreatedAt", "CreatedBy", "IsActive", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("70000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000005"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null },
                    { new Guid("70000000-0000-0000-0000-000000000004"), new Guid("40000000-0000-0000-0000-000000000005"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null },
                    { new Guid("70000000-0000-0000-0000-000000000004"), new Guid("40000000-0000-0000-0000-000000000006"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null }
                });

            migrationBuilder.InsertData(
                table: "UserStoreAssignments",
                columns: new[] { "StoreId", "UserId", "CreatedAt", "CreatedBy", "EffectiveFrom", "EffectiveTo", "IsActive", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000005"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, null },
                    { new Guid("30000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000005"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_IsActive",
                table: "RolePermissions",
                columns: new[] { "RoleId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Code",
                table: "Organizations",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId_IsActive_UserId",
                table: "UserRoles",
                columns: new[] { "RoleId", "IsActive", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserStoreAssignments_StoreId_IsActive_UserId",
                table: "UserStoreAssignments",
                columns: new[] { "StoreId", "IsActive", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserStoreAssignments");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId_IsActive",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_Code",
                table: "Organizations");

            migrationBuilder.DeleteData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: new Guid("50000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000006"), new Guid("70000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000007"), new Guid("70000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000008"), new Guid("70000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("71000000-0000-0000-0000-000000000008"), new Guid("70000000-0000-0000-0000-000000000004") });

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000005"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000006"));

            migrationBuilder.DeleteData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "AuthorizationPermissions",
                keyColumn: "Id",
                keyValue: new Guid("71000000-0000-0000-0000-000000000008"));

            migrationBuilder.DeleteData(
                table: "AuthorizationRoles",
                keyColumn: "Id",
                keyValue: new Guid("70000000-0000-0000-0000-000000000004"));

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AuthorizationRoles");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "AuthorizationRoles");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "AuthorizationRoles");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "AuthorizationRoles");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "AuthorizationPermissions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "AuthorizationPermissions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "AuthorizationPermissions");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "AuthorizationPermissions");

            migrationBuilder.UpdateData(
                table: "AuthorizationRoles",
                keyColumn: "Id",
                keyValue: new Guid("70000000-0000-0000-0000-000000000003"),
                columns: new[] { "Code", "Description", "Name" },
                values: new object[] { "Admin", "Platform administrator role", "Administrator" });
        }
    }
}
