using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "aip_expenditures",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_id",
                table: "aip_expenditures",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "aip_activities",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "aip_activities",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_id",
                table: "aip_activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_expenditures_updated_by_id",
                table: "aip_expenditures",
                column: "updated_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_activities_updated_by_id",
                table: "aip_activities",
                column: "updated_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_aip_activities_users_updated_by_id",
                table: "aip_activities",
                column: "updated_by_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_aip_expenditures_users_updated_by_id",
                table: "aip_expenditures",
                column: "updated_by_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_activities_users_updated_by_id",
                table: "aip_activities");

            migrationBuilder.DropForeignKey(
                name: "FK_aip_expenditures_users_updated_by_id",
                table: "aip_expenditures");

            migrationBuilder.DropIndex(
                name: "IX_aip_expenditures_updated_by_id",
                table: "aip_expenditures");

            migrationBuilder.DropIndex(
                name: "IX_aip_activities_updated_by_id",
                table: "aip_activities");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "aip_expenditures");

            migrationBuilder.DropColumn(
                name: "updated_by_id",
                table: "aip_expenditures");

            migrationBuilder.DropColumn(
                name: "row_version",
                table: "aip_activities");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "aip_activities");

            migrationBuilder.DropColumn(
                name: "updated_by_id",
                table: "aip_activities");
        }
    }
}
