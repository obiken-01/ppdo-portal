using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the old plain-unique email index before making email nullable.
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            // 2. Make Email nullable (was required before this migration).
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            // 3. Add Username as temporarily nullable so the backfill can run before
            //    the NOT NULL constraint and unique index are applied.
            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // 4. Backfill Username from email local-part for all existing users.
            //    De-duplicates by appending a counter (ROW_NUMBER) when two users share
            //    the same local-part (e.g. john@ppdo.gov.ph and john@lgu.gov.ph → john, john2).
            //    Users with no email are left NULL here — they will be handled separately.
            migrationBuilder.Sql(@"
WITH numbered AS (
    SELECT Id,
           LOWER(SUBSTRING(Email, 1, CHARINDEX('@', Email) - 1)) AS base,
           ROW_NUMBER() OVER (
               PARTITION BY LOWER(SUBSTRING(Email, 1, CHARINDEX('@', Email) - 1))
               ORDER BY CreatedAt
           ) AS rn
    FROM Users
    WHERE Email IS NOT NULL
)
UPDATE u
SET u.Username = CASE WHEN n.rn = 1 THEN n.base ELSE n.base + CAST(n.rn AS nvarchar(10)) END
FROM Users u
JOIN numbered n ON u.Id = n.Id;
");

            // 5. Override SuperAdmin's username to 'superadmin' (seed value).
            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "Username",
                value: "superadmin");

            // 6. Now all rows have a Username — enforce NOT NULL.
            migrationBuilder.AlterColumn<string>(
                name: "Username",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            // 7. Unique index on Username (no filter — always required).
            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            // 8. Filtered unique index on Email — allows multiple NULL values.
            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Remove both new indexes.
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_Username",
                table: "Users");

            // 2. Drop the Username column (first relax to nullable so EF doesn't complain).
            migrationBuilder.DropColumn(
                name: "Username",
                table: "Users");

            // 3. Restore Email as NOT NULL.
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            // 4. Restore the plain unique index on Email.
            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }
    }
}
