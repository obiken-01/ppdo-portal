using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// PPDO-130 T1 (PPDO-147): <c>aip_activities.division_id</c>, the <c>aip_division_submissions</c>
    /// table, and the FY2028+ tag backfill. Spec: <c>docs/v1.8/Division_Submit_Spec.md</c> §5.
    ///
    /// ⚠️ Additive. No existing value changes except <c>aip_activities.division_id</c>, which is
    /// brand new and starts null. <see cref="Down"/> drops both, and the tags go with them.
    /// </summary>
    public partial class AddAipDivisionSubmit : Migration
    {
        /// <summary>
        /// The tag backfill (spec §5), public so <c>AipDivisionBackfillTests</c> runs the exact
        /// statement the migration runs.
        ///
        /// <para>
        /// An untagged FY2028+ activity gets its program's division <b>only when exactly one active
        /// division</b> of the activity's own office is assigned to that program in
        /// <c>program_divisions</c>. Two or more stay null on purpose: picking one would invent the
        /// attribution this ticket exists to make explicit. Those are left for the department head
        /// to tag (spec decision 4).
        /// </para>
        ///
        /// <para>
        /// ⚠️ <b>2028 is written out, not read from <c>AipFiscalYears.FirstEnteredFiscalYear</c>.</b>
        /// A migration must mean the same thing forever. If the constant ever moved, a later fresh
        /// database would backfill different years than production did.
        /// </para>
        ///
        /// <para>
        /// Written as a correlated subquery rather than T-SQL's <c>UPDATE … FROM</c>, so the same
        /// text runs on SQL Server and on the SQLite test database. <c>COUNT(DISTINCT …)</c>
        /// because <c>program_divisions</c> is only unique per (office_ref_code, program_ref_code,
        /// division_id), so one division can appear twice for the same config office. Re-running it
        /// is a no-op: it touches only rows that are still null.
        /// </para>
        /// </summary>
        public const string BackfillSql = """
            UPDATE aip_activities
            SET division_id = (
                SELECT CASE WHEN COUNT(DISTINCT pd.division_id) = 1 THEN MIN(pd.division_id) END
                FROM aip_projects p
                INNER JOIN aip_programs g       ON g.id = p.program_id
                INNER JOIN aip_offices o        ON o.id = g.office_id
                INNER JOIN program_divisions pd ON pd.office_id = o.office_id
                                               AND pd.program_ref_code = g.ref_code
                INNER JOIN divisions d          ON d.id = pd.division_id
                                               AND d.office_id = o.office_id
                                               AND d.is_active = 1
                WHERE p.id = aip_activities.project_id)
            WHERE division_id IS NULL
              AND project_id IN (
                SELECT p.id
                FROM aip_projects p
                INNER JOIN aip_programs g ON g.id = p.program_id
                INNER JOIN aip_offices o  ON o.id = g.office_id
                INNER JOIN aip_records r  ON r.id = o.aip_record_id
                WHERE r.fiscal_year >= 2028);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "division_id",
                table: "aip_activities",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "aip_division_submissions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    aip_record_id = table.Column<int>(type: "int", nullable: false),
                    office_id = table.Column<int>(type: "int", nullable: false),
                    division_id = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    submitted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    returned_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    returned_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_division_submissions", x => x.id);
                    table.CheckConstraint("CK_aip_division_submissions_status", "[status] IN ('Draft', 'Submitted')");
                    table.ForeignKey(
                        name: "FK_aip_division_submissions_aip_records_aip_record_id",
                        column: x => x.aip_record_id,
                        principalTable: "aip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_aip_division_submissions_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_division_submissions_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_division_submissions_users_returned_by_user_id",
                        column: x => x.returned_by_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_division_submissions_users_submitted_by_user_id",
                        column: x => x.submitted_by_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aip_activities_division_id",
                table: "aip_activities",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_submissions_division_id",
                table: "aip_division_submissions",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_submissions_office_id",
                table: "aip_division_submissions",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_submissions_record_office",
                table: "aip_division_submissions",
                columns: new[] { "aip_record_id", "office_id" });

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_submissions_returned_by_user_id",
                table: "aip_division_submissions",
                column: "returned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_submissions_submitted_by_user_id",
                table: "aip_division_submissions",
                column: "submitted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "UX_aip_division_submissions_record_division",
                table: "aip_division_submissions",
                columns: new[] { "aip_record_id", "division_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_aip_activities_divisions_division_id",
                table: "aip_activities",
                column: "division_id",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Last, once the column and its FK exist: the FK checks every value written here.
            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_activities_divisions_division_id",
                table: "aip_activities");

            migrationBuilder.DropTable(
                name: "aip_division_submissions");

            migrationBuilder.DropIndex(
                name: "IX_aip_activities_division_id",
                table: "aip_activities");

            migrationBuilder.DropColumn(
                name: "division_id",
                table: "aip_activities");
        }
    }
}
