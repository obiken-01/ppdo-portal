using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetPlanningPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Division",
                table: "Users",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "OfficeId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideCanAccessBudgetPlanning",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideCanManageConfig",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverrideCanUploadAip",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanAccessBudgetPlanning",
                table: "PermissionGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageConfig",
                table: "PermissionGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanUploadAip",
                table: "PermissionGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                column: "CanAccessBudgetPlanning",
                value: true);

            migrationBuilder.UpdateData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000002"),
                column: "CanAccessBudgetPlanning",
                value: true);

            migrationBuilder.UpdateData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000003"),
                column: "CanAccessBudgetPlanning",
                value: true);

            migrationBuilder.UpdateData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000004"),
                column: "CanAccessBudgetPlanning",
                value: true);

            migrationBuilder.UpdateData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000005"),
                column: "CanAccessBudgetPlanning",
                value: true);

            // Observer Default (…0006) keeps all new flags false — no UpdateData needed.
            // (The EF scaffolder emitted an empty UpdateData here, which produces invalid
            // "SET WHERE" SQL; removed by hand.)

            migrationBuilder.InsertData(
                table: "PermissionGroups",
                columns: new[] { "Id", "CanAccessBudgetPlanning", "CreatedAt", "Description", "Division", "Name", "UpdatedAt" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000007"), true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, null, "Office User Default", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                columns: new[] { "OfficeId", "OverrideCanAccessBudgetPlanning", "OverrideCanManageConfig", "OverrideCanUploadAip" },
                values: new object[] { null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Users_OfficeId",
                table: "Users",
                column: "OfficeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_offices_OfficeId",
                table: "Users",
                column: "OfficeId",
                principalTable: "offices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_offices_OfficeId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_OfficeId",
                table: "Users");

            migrationBuilder.DeleteData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000007"));

            migrationBuilder.DropColumn(
                name: "OfficeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OverrideCanAccessBudgetPlanning",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OverrideCanManageConfig",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OverrideCanUploadAip",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CanAccessBudgetPlanning",
                table: "PermissionGroups");

            migrationBuilder.DropColumn(
                name: "CanManageConfig",
                table: "PermissionGroups");

            migrationBuilder.DropColumn(
                name: "CanUploadAip",
                table: "PermissionGroups");

            migrationBuilder.AlterColumn<int>(
                name: "Division",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
