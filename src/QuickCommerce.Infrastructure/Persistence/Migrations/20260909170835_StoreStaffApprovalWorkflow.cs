using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace QuickCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreStaffApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StaffCategory",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovalType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000001"),
                column: "StaffCategory",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000002"),
                column: "StaffCategory",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000003"),
                column: "StaffCategory",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000004"),
                column: "StaffCategory",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000005"),
                column: "StaffCategory",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000006"),
                column: "StaffCategory",
                value: null);

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "CreatedBy", "DisplayName", "Email", "ExternalSubject", "FirstName", "IsActive", "LastName", "OrganizationId", "Role", "StaffCategory", "StoreId", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("40000000-0000-0000-0000-000000000007"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Demo Store Manager", "storemanager@example.test", "storemanager", "Demo", true, "Store Manager", new Guid("00000000-0000-0000-0000-000000000001"), "StoreStaff", "StoreManager", new Guid("30000000-0000-0000-0000-000000000001"), null, null },
                    { new Guid("40000000-0000-0000-0000-000000000008"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", "Demo Store Employee", "storeemployee@example.test", "storeemployee", "Demo", true, "Store Employee", new Guid("00000000-0000-0000-0000-000000000001"), "StoreStaff", "StoreEmployee", new Guid("30000000-0000-0000-0000-000000000001"), null, null }
                });

            migrationBuilder.InsertData(
                table: "UserRoles",
                columns: new[] { "RoleId", "UserId", "CreatedAt", "CreatedBy", "IsActive", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("70000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000007"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null },
                    { new Guid("70000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000008"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", true, null, null }
                });

            migrationBuilder.InsertData(
                table: "UserStoreAssignments",
                columns: new[] { "StoreId", "UserId", "CreatedAt", "CreatedBy", "EffectiveFrom", "EffectiveTo", "IsActive", "UpdatedAt", "UpdatedBy" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000007"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, null },
                    { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000008"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "seed", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ApprovedByUserId",
                table: "ApprovalRequests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_OrganizationId_StoreId_Status_CreatedAt",
                table: "ApprovalRequests",
                columns: new[] { "OrganizationId", "StoreId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_RequestedByUserId",
                table: "ApprovalRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_StoreId",
                table: "ApprovalRequests",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DeleteData(
                table: "UserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { new Guid("70000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000007") });

            migrationBuilder.DeleteData(
                table: "UserRoles",
                keyColumns: new[] { "RoleId", "UserId" },
                keyValues: new object[] { new Guid("70000000-0000-0000-0000-000000000002"), new Guid("40000000-0000-0000-0000-000000000008") });

            migrationBuilder.DeleteData(
                table: "UserStoreAssignments",
                keyColumns: new[] { "StoreId", "UserId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000007") });

            migrationBuilder.DeleteData(
                table: "UserStoreAssignments",
                keyColumns: new[] { "StoreId", "UserId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("40000000-0000-0000-0000-000000000008") });

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000007"));

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("40000000-0000-0000-0000-000000000008"));

            migrationBuilder.DropColumn(
                name: "StaffCategory",
                table: "Users");
        }
    }
}
