using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDivisionsRetirePermissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_PermissionGroups_GroupId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "PermissionGroups");

            migrationBuilder.DropIndex(
                name: "IX_Users_GroupId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "Division",
                table: "Users",
                newName: "DivisionId");

            migrationBuilder.RenameColumn(
                name: "Division",
                table: "PurchaseRequests",
                newName: "DivisionId");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseRequests_Division",
                table: "PurchaseRequests",
                newName: "IX_PurchaseRequests_DivisionId");

            migrationBuilder.RenameColumn(
                name: "Division",
                table: "Distributions",
                newName: "DivisionId");

            migrationBuilder.AddColumn<bool>(
                name: "OverrideCanManageAllocation",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "divisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    office_id = table.Column<int>(type: "int", nullable: false),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    can_access_inventory = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_access_reports = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_manage_users = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_manage_resource_links = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_access_budget_planning = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_upload_aip = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_manage_config = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_divisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_divisions_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                columns: new[] { "DivisionId", "OverrideCanManageAllocation" },
                values: new object[] { null, null });

            // ── Data cleanup for the model change (v1.2 — RAL-97) ──────────────────
            // 1) The Observer role is retired — convert any existing Observer (Role=3) to Staff (Role=2).
            migrationBuilder.Sql("UPDATE [Users] SET [Role] = 2 WHERE [Role] = 3;");
            // 2) The renamed DivisionId columns still hold the OLD Division enum ints (0–4), which are not
            //    valid divisions.id values. Null them so the new FK can be enforced; divisions are seeded
            //    via CSV afterwards and each user is reassigned a division manually.
            migrationBuilder.Sql("UPDATE [Users] SET [DivisionId] = NULL;");
            // NOTE: PurchaseRequests/Distributions must be EMPTY before applying (inventory clean-slate —
            //       prod inventory is empty; wipe local inventory data first). Their DivisionId is NOT NULL
            //       and the new FK to divisions would otherwise reject the stale enum values.

            migrationBuilder.CreateIndex(
                name: "IX_Users_DivisionId",
                table: "Users",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Distributions_DivisionId",
                table: "Distributions",
                column: "DivisionId");

            migrationBuilder.CreateIndex(
                name: "IX_divisions_office_id",
                table: "divisions",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_divisions_office_id_name",
                table: "divisions",
                columns: new[] { "office_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Distributions_divisions_DivisionId",
                table: "Distributions",
                column: "DivisionId",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseRequests_divisions_DivisionId",
                table: "PurchaseRequests",
                column: "DivisionId",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_divisions_DivisionId",
                table: "Users",
                column: "DivisionId",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Distributions_divisions_DivisionId",
                table: "Distributions");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseRequests_divisions_DivisionId",
                table: "PurchaseRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_divisions_DivisionId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "divisions");

            migrationBuilder.DropIndex(
                name: "IX_Users_DivisionId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Distributions_DivisionId",
                table: "Distributions");

            migrationBuilder.DropColumn(
                name: "OverrideCanManageAllocation",
                table: "Users");

            migrationBuilder.RenameColumn(
                name: "DivisionId",
                table: "Users",
                newName: "Division");

            migrationBuilder.RenameColumn(
                name: "DivisionId",
                table: "PurchaseRequests",
                newName: "Division");

            migrationBuilder.RenameIndex(
                name: "IX_PurchaseRequests_DivisionId",
                table: "PurchaseRequests",
                newName: "IX_PurchaseRequests_Division");

            migrationBuilder.RenameColumn(
                name: "DivisionId",
                table: "Distributions",
                newName: "Division");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PermissionGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanAccessBudgetPlanning = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanAccessInventory = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanAccessReports = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanManageConfig = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanManageResourceLinks = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanManageUsers = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CanUploadAip = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Division = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionGroups", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PermissionGroups",
                columns: new[] { "Id", "CanAccessBudgetPlanning", "CanAccessInventory", "CanAccessReports", "CanManageResourceLinks", "CreatedAt", "Description", "Division", "Name", "UpdatedAt" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000001"), true, true, true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 0, "Admin Division Staff", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "PermissionGroups",
                columns: new[] { "Id", "CanAccessBudgetPlanning", "CanAccessReports", "CreatedAt", "Description", "Division", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000002"), true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1, "Planning Staff", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-0000-0000-000000000003"), true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 2, "RM Staff", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-0000-0000-000000000004"), true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 3, "MIS Staff", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("10000000-0000-0000-0000-000000000005"), true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 4, "SPD Staff", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.InsertData(
                table: "PermissionGroups",
                columns: new[] { "Id", "CreatedAt", "Description", "Division", "Name", "UpdatedAt" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000006"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Observer Default", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.InsertData(
                table: "PermissionGroups",
                columns: new[] { "Id", "CanAccessBudgetPlanning", "CreatedAt", "Description", "Division", "Name", "UpdatedAt" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000007"), true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Office User Default", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                columns: new[] { "Division", "GroupId" },
                values: new object[] { 0, null });

            migrationBuilder.CreateIndex(
                name: "IX_Users_GroupId",
                table: "Users",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionGroups_Name",
                table: "PermissionGroups",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_PermissionGroups_GroupId",
                table: "Users",
                column: "GroupId",
                principalTable: "PermissionGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
