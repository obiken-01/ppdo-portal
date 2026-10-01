using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lce_name",
                table: "investment_planning_settings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lce_position",
                table: "investment_planning_settings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ppdc_name",
                table: "investment_planning_settings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ppdc_position",
                table: "investment_planning_settings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "investment_proposals",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    aip_project_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "Draft"),
                    project_location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    hgdg_checklist = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    hgdg_score = table.Column<decimal>(type: "decimal(3,1)", precision: 3, scale: 1, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    rationale = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    general_objective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    partnership_sustainability = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    womens_impact_strategy = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    project_supervisor = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    project_manager = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    direct_same_as_summary = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    signatory1_label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    signatory1_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    signatory1_position = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    signatory2_label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    signatory2_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    signatory2_position = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    signatory3_label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    signatory3_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    signatory3_position = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    snapshot_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    finalized_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    finalized_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposals", x => x.id);
                    table.CheckConstraint("CK_investment_proposals_status", "[status] IN ('Draft', 'Final')");
                    table.ForeignKey(
                        name: "FK_investment_proposals_aip_projects_aip_project_id",
                        column: x => x.aip_project_id,
                        principalTable: "aip_projects",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_investment_proposals_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investment_proposals_users_finalized_by_user_id",
                        column: x => x.finalized_by_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_investment_proposals_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_beneficiaries",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    section = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    label = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    male = table.Column<int>(type: "int", nullable: true),
                    female = table.Column<int>(type: "int", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_beneficiaries", x => x.id);
                    table.CheckConstraint("CK_investment_proposal_beneficiaries_section", "[section] IN ('Summary', 'Direct', 'Indirect')");
                    table.ForeignKey(
                        name: "FK_investment_proposal_beneficiaries_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_benefits",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    sector = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    benefit = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    cost = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_benefits", x => x.id);
                    table.ForeignKey(
                        name: "FK_investment_proposal_benefits_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_capacity_trainings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    member_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    training = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "investment_proposal_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    label = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_investment_proposal_groups_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_logframe",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    level = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    target = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    verification = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_logframe", x => x.id);
                    table.CheckConstraint("CK_investment_proposal_logframe_level", "[level] IN ('Impact', 'Outcome', 'Output', 'Input')");
                    table.ForeignKey(
                        name: "FK_investment_proposal_logframe_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_monitoring",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    phase = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    activity = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    schedule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    tools = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_monitoring", x => x.id);
                    table.CheckConstraint("CK_investment_proposal_monitoring_phase", "[phase] IN ('Pre', 'During', 'Post')");
                    table.ForeignKey(
                        name: "FK_investment_proposal_monitoring_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_risks",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    risk = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    prevention = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    monitoring = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_risks", x => x.id);
                    table.ForeignKey(
                        name: "FK_investment_proposal_risks_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_team_members",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    sex = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: false),
                    gad_trainings = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    expertise = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_team_members", x => x.id);
                    table.CheckConstraint("CK_investment_proposal_team_members_sex", "[sex] IN ('M', 'F')");
                    table.ForeignKey(
                        name: "FK_investment_proposal_team_members_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "investment_proposal_work_plan_rows",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    proposal_id = table.Column<int>(type: "int", nullable: false),
                    aip_activity_id = table.Column<int>(type: "int", nullable: true),
                    group_id = table.Column<int>(type: "int", nullable: true),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    performance_target = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    gender_issues = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    timeline = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    opr = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_proposal_work_plan_rows", x => x.id);
                    table.CheckConstraint("CK_investment_proposal_work_plan_rows_activity_or_name", "[aip_activity_id] IS NOT NULL OR [name] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_investment_proposal_work_plan_rows_aip_activities_aip_activity_id",
                        column: x => x.aip_activity_id,
                        principalTable: "aip_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_investment_proposal_work_plan_rows_investment_proposal_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "investment_proposal_groups",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_investment_proposal_work_plan_rows_investment_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalTable: "investment_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "investment_planning_settings",
                keyColumn: "id",
                keyValue: 1,
                columns: new[] { "lce_name", "lce_position", "ppdc_name", "ppdc_position" },
                values: new object[] { null, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_beneficiaries_proposal_id",
                table: "investment_proposal_beneficiaries",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "UX_investment_proposal_benefits_proposal_sector",
                table: "investment_proposal_benefits",
                columns: new[] { "proposal_id", "sector" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_capacity_trainings_proposal_id",
                table: "investment_proposal_capacity_trainings",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_groups_proposal_id",
                table: "investment_proposal_groups",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "UX_investment_proposal_logframe_proposal_level",
                table: "investment_proposal_logframe",
                columns: new[] { "proposal_id", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_monitoring_proposal_id",
                table: "investment_proposal_monitoring",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_risks_proposal_id",
                table: "investment_proposal_risks",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_team_members_proposal_id",
                table: "investment_proposal_team_members",
                column: "proposal_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_work_plan_rows_aip_activity_id",
                table: "investment_proposal_work_plan_rows",
                column: "aip_activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposal_work_plan_rows_group_id",
                table: "investment_proposal_work_plan_rows",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "UX_investment_proposal_work_plan_rows_proposal_activity",
                table: "investment_proposal_work_plan_rows",
                columns: new[] { "proposal_id", "aip_activity_id" },
                unique: true,
                filter: "[aip_activity_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposals_created_by_user_id",
                table: "investment_proposals",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposals_finalized_by_user_id",
                table: "investment_proposals",
                column: "finalized_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_investment_proposals_updated_by_user_id",
                table: "investment_proposals",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "UX_investment_proposals_aip_project_id",
                table: "investment_proposals",
                column: "aip_project_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_proposal_beneficiaries");

            migrationBuilder.DropTable(
                name: "investment_proposal_benefits");

            migrationBuilder.DropTable(
                name: "investment_proposal_capacity_trainings");

            migrationBuilder.DropTable(
                name: "investment_proposal_logframe");

            migrationBuilder.DropTable(
                name: "investment_proposal_monitoring");

            migrationBuilder.DropTable(
                name: "investment_proposal_risks");

            migrationBuilder.DropTable(
                name: "investment_proposal_team_members");

            migrationBuilder.DropTable(
                name: "investment_proposal_work_plan_rows");

            migrationBuilder.DropTable(
                name: "investment_proposal_groups");

            migrationBuilder.DropTable(
                name: "investment_proposals");

            migrationBuilder.DropColumn(
                name: "lce_name",
                table: "investment_planning_settings");

            migrationBuilder.DropColumn(
                name: "lce_position",
                table: "investment_planning_settings");

            migrationBuilder.DropColumn(
                name: "ppdc_name",
                table: "investment_planning_settings");

            migrationBuilder.DropColumn(
                name: "ppdc_position",
                table: "investment_planning_settings");
        }
    }
}
