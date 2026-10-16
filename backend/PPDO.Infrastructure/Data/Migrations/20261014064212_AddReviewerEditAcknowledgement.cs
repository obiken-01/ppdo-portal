using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewerEditAcknowledgement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "acknowledged_at",
                table: "aip_review_comments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "acknowledged_by_id",
                table: "aip_review_comments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_reviewer_edit",
                table: "aip_review_comments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_aip_review_comments_acknowledged_by_id",
                table: "aip_review_comments",
                column: "acknowledged_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_review_comments_office_reviewer_edit_ack",
                table: "aip_review_comments",
                columns: new[] { "aip_office_id", "is_reviewer_edit", "acknowledged_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_aip_review_comments_Users_acknowledged_by_id",
                table: "aip_review_comments",
                column: "acknowledged_by_id",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_review_comments_Users_acknowledged_by_id",
                table: "aip_review_comments");

            migrationBuilder.DropIndex(
                name: "IX_aip_review_comments_acknowledged_by_id",
                table: "aip_review_comments");

            migrationBuilder.DropIndex(
                name: "IX_aip_review_comments_office_reviewer_edit_ack",
                table: "aip_review_comments");

            migrationBuilder.DropColumn(
                name: "acknowledged_at",
                table: "aip_review_comments");

            migrationBuilder.DropColumn(
                name: "acknowledged_by_id",
                table: "aip_review_comments");

            migrationBuilder.DropColumn(
                name: "is_reviewer_edit",
                table: "aip_review_comments");
        }
    }
}
