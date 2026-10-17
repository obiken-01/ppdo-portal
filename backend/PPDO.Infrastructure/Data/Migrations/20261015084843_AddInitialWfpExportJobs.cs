using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInitialWfpExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "background_job_ids");

            migrationBuilder.CreateTable(
                name: "initial_wfp_export_jobs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR [background_job_ids]"),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    requested_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    claimed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    heartbeat_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    finished_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    done_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    total_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    problems_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    file_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initial_wfp_export_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_initial_wfp_export_jobs_Users_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_initial_wfp_export_jobs_fiscal_year_active",
                table: "initial_wfp_export_jobs",
                column: "fiscal_year",
                unique: true,
                filter: "[status] IN (N'Queued', N'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_initial_wfp_export_jobs_fiscal_year_status",
                table: "initial_wfp_export_jobs",
                columns: new[] { "fiscal_year", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_initial_wfp_export_jobs_requested_by_id",
                table: "initial_wfp_export_jobs",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_initial_wfp_export_jobs_status_created_at",
                table: "initial_wfp_export_jobs",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "initial_wfp_export_jobs");

            migrationBuilder.DropSequence(
                name: "background_job_ids");
        }
    }
}
