using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignInvestmentProposalsWithTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_proposal_capacity_trainings");

            migrationBuilder.AddColumn<string>(
                name: "signatory4_label",
                table: "investment_proposals",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory4_name",
                table: "investment_proposals",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signatory4_position",
                table: "investment_proposals",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "required_training",
                table: "investment_proposal_team_members",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_investment_proposal_benefits_sector",
                table: "investment_proposal_benefits",
                sql: "[sector] IN ('Social', 'Economic', 'Environmental', 'Institutional', 'Infrastructure/Land Use')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_investment_proposal_benefits_sector",
                table: "investment_proposal_benefits");

            migrationBuilder.DropColumn(
                name: "signatory4_label",
                table: "investment_proposals");

            migrationBuilder.DropColumn(
                name: "signatory4_name",
                table: "investment_proposals");

            migrationBuilder.DropColumn(
                name: "signatory4_position",
                table: "investment_proposals");

            migrationBuilder.DropColumn(
                name: "required_training",
                table: "investment_proposal_team_members");

            migrationBuilder.CreateTable(
                name: "investment_proposal_capacity_trainings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    member_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    training = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_capacity_trainings", x => x.id);
                    table.ForeignKey(
                        name: "FK_investment_proposal_capacity_trainings_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_capacity_trainings_proposal_id",
                table: "investment_proposal_capacity_trainings",
                column: "proposal_id");
        }
    }
}
