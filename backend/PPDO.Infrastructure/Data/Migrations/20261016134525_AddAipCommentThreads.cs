using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipCommentThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "parent_comment_id",
                table: "aip_review_comments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_review_comments_parent_comment_id",
                table: "aip_review_comments",
                column: "parent_comment_id");

            migrationBuilder.AddForeignKey(
                name: "FK_aip_review_comments_aip_review_comments_parent_comment_id",
                table: "aip_review_comments",
                column: "parent_comment_id",
                principalTable: "aip_review_comments",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_review_comments_aip_review_comments_parent_comment_id",
                table: "aip_review_comments");

            migrationBuilder.DropIndex(
                name: "IX_aip_review_comments_parent_comment_id",
                table: "aip_review_comments");

            migrationBuilder.DropColumn(
                name: "parent_comment_id",
                table: "aip_review_comments");
        }
    }
}
