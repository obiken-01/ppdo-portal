using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipReviewComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aip_review_comments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    aip_office_id = table.Column<int>(type: "int", nullable: false),
                    node_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    node_id = table.Column<int>(type: "int", nullable: false),
                    author_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_side = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolved_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_review_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_review_comments_Users_author_id",
                        column: x => x.author_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_review_comments_Users_resolved_by_id",
                        column: x => x.resolved_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_review_comments_aip_offices_aip_office_id",
                        column: x => x.aip_office_id,
                        principalTable: "aip_offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aip_review_comments_author_id",
                table: "aip_review_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_review_comments_office_resolved",
                table: "aip_review_comments",
                columns: new[] { "aip_office_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "IX_aip_review_comments_resolved_by_id",
                table: "aip_review_comments",
                column: "resolved_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aip_review_comments");
        }
    }
}
