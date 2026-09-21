using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LandingPage",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "landing_page",
                table: "offices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "landing_page",
                table: "divisions",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "LandingPage",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LandingPage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "landing_page",
                table: "offices");

            migrationBuilder.DropColumn(
                name: "landing_page",
                table: "divisions");
        }
    }
}
