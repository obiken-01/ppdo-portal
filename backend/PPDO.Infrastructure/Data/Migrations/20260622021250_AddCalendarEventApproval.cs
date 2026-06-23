using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarEventApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "CalendarEvents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "CalendarEvents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedById",
                table: "CalendarEvents",
                type: "uniqueidentifier",
                nullable: true);

            // defaultValue: 1 (Approved) backfills all existing rows so events already on
            // users' calendars stay visible. EF always sends an explicit Status on insert, so
            // this DB default has no runtime effect; it is dropped with the column on rollback.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CalendarEvents",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_ReviewedById",
                table: "CalendarEvents",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_Status",
                table: "CalendarEvents",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_CalendarEvents_Users_ReviewedById",
                table: "CalendarEvents",
                column: "ReviewedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalendarEvents_Users_ReviewedById",
                table: "CalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEvents_ReviewedById",
                table: "CalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEvents_Status",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CalendarEvents");
        }
    }
}
