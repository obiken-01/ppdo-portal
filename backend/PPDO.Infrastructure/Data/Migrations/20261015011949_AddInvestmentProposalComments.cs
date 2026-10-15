using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentProposalComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investment_proposal_comments",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    parent_comment_id = table.Column<int>(type: "int", nullable: true),
                    body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    author_side = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    author_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    resolved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    resolved_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_investment_proposal_comments_Users_author_id",
                        column: x => x.author_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investment_proposal_comments_Users_resolved_by_id",
                        column: x => x.resolved_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investment_proposal_comments_investment_proposal_comments_parent_comment_id",
                        column: x => x.parent_comment_id,
                        principalTable: "investment_proposal_comments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_investment_proposal_comments_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_comments_author_id",
                table: "investment_proposal_comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_comments_parent_comment_id",
                table: "investment_proposal_comments",
                column: "parent_comment_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_comments_proposal_resolved",
                table: "investment_proposal_comments",
                columns: new[] { "proposal_id", "resolved_at" });

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_comments_resolved_by_id",
                table: "investment_proposal_comments",
                column: "resolved_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_proposal_comments");
        }
    }
}
