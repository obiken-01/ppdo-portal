using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramDivisionOfficeFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "office_id",
                table: "program_divisions",
                type: "int",
                nullable: true);

            // Backfill from the ref-code suffix match the service used to do in memory
            // (AllocationService.GetProgramAssignmentsAsync: aipOffice.RefCode ends with
            // offices.office_ref_code, e.g. "1000-000-1-01-010" -> "01-010").
            //
            // CROSS APPLY with TOP 1 ORDER BY LEN DESC, not a JOIN: two config offices can both
            // be suffixes of the same AIP ref code, and a JOIN would then duplicate the row into
            // an ambiguous double-assignment. Longest match wins, which is the specific office.
            //
            // Rows that match nothing keep office_id NULL rather than being dropped — RAL-249
            // requires exceptions to be reported, not silently discarded. Find them with:
            //     SELECT * FROM program_divisions WHERE office_id IS NULL;
            // and resolve them before the follow-up that makes the column NOT NULL.
            migrationBuilder.Sql(@"
                UPDATE pd
                SET    pd.office_id = m.id
                FROM   program_divisions AS pd
                CROSS APPLY (
                    SELECT TOP 1 o.id
                    FROM   offices AS o
                    WHERE  o.office_ref_code IS NOT NULL
                      AND  LEN(o.office_ref_code) > 0
                      AND  RIGHT(pd.office_ref_code, LEN(o.office_ref_code)) = o.office_ref_code
                    ORDER BY LEN(o.office_ref_code) DESC
                ) AS m
                WHERE  pd.office_id IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_program_divisions_office_program",
                table: "program_divisions",
                columns: new[] { "office_id", "program_ref_code" });

            migrationBuilder.AddForeignKey(
                name: "FK_program_divisions_offices_office_id",
                table: "program_divisions",
                column: "office_id",
                principalTable: "offices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_program_divisions_offices_office_id",
                table: "program_divisions");

            migrationBuilder.DropIndex(
                name: "IX_program_divisions_office_program",
                table: "program_divisions");

            migrationBuilder.DropColumn(
                name: "office_id",
                table: "program_divisions");
        }
    }
}
