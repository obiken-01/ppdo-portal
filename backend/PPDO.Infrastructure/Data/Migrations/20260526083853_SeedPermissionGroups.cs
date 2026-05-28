using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedPermissionGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "ContactNo", "CreatedAt", "Division", "Email", "FullName", "GroupId", "IsActive", "OverrideCanAccessInventory", "OverrideCanAccessReports", "OverrideCanManageUsers", "PasswordHash", "Position", "Role", "UpdatedAt" },
                values: new object[] { new Guid("20000000-0000-0000-0000-000000000001"), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0, "superadmin@ppdo.gov.ph", "System Administrator", null, true, null, null, null, "$2a$11$HaBMPo0zwTrOTJt3jqY8Ou8RNcYTfedkTJCDuP2AW5RFvofq0wQEO", "System Administrator", 0, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"));
        }
    }
}
