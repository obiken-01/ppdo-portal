using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// V18-32 (PPDO-33) — AIP ownership becomes a real foreign key.
    ///
    /// <para>
    /// Additive: adds <c>aip_offices.office_id</c> and backfills it. No existing value is
    /// overwritten, unlike <c>MigrateAipAmountsToPesos</c>. What it can get wrong is leaving rows
    /// unmatched, which is visible and fixable rather than destructive — see the query below.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>The backfill is the last use of the ref-code suffix rule for ownership.</b> After this
    /// runs, reads match on <c>office_id</c>; <c>ref_code</c> stays as the AIP-side re-link key and
    /// as the record of what the backfill matched on.
    /// </para>
    /// </summary>
    public partial class AddAipOfficeOwnershipFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "office_id",
                table: "aip_offices",
                type: "int",
                nullable: true);

            // Backfill from the ref-code suffix match every scoped read used to do in memory
            // (aip_offices.ref_code ends with offices.office_ref_code, e.g.
            // "1000-000-1-01-010" -> "01-010"). Same rule, same shape, as RAL-249's backfill of
            // program_divisions.office_id — deliberately, so a row matched there and a row matched
            // here resolve to the same office.
            //
            // CROSS APPLY with TOP 1 ORDER BY LEN DESC, not a JOIN: two config offices can both be
            // suffixes of one AIP ref code, and a JOIN would duplicate the row into an ambiguous
            // double-ownership. Longest match wins, which is the more specific office.
            //
            // ⚠️ Rows matching nothing keep office_id NULL rather than being dropped. Record the
            // count before and after — an unmatched row is invisible to every scoped read, so it
            // must be found and resolved, not discovered later by a user seeing an empty page:
            //
            //     SELECT r.fiscal_year, COUNT(*) AS unmatched
            //     FROM   aip_offices a JOIN aip_records r ON r.id = a.aip_record_id
            //     WHERE  a.office_id IS NULL
            //     GROUP  BY r.fiscal_year;
            migrationBuilder.Sql(@"
                UPDATE ao
                SET    ao.office_id = m.id
                FROM   aip_offices AS ao
                CROSS APPLY (
                    SELECT TOP 1 o.id
                    FROM   offices AS o
                    WHERE  o.office_ref_code IS NOT NULL
                      AND  LEN(o.office_ref_code) > 0
                      AND  RIGHT(ao.ref_code, LEN(o.office_ref_code)) = o.office_ref_code
                    ORDER BY LEN(o.office_ref_code) DESC
                ) AS m
                WHERE  ao.office_id IS NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_aip_record_id_office_id",
                table: "aip_offices",
                columns: new[] { "aip_record_id", "office_id" });

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_office_id",
                table: "aip_offices",
                column: "office_id");

            migrationBuilder.AddForeignKey(
                name: "FK_aip_offices_offices_office_id",
                table: "aip_offices",
                column: "office_id",
                principalTable: "offices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_offices_offices_office_id",
                table: "aip_offices");

            migrationBuilder.DropIndex(
                name: "IX_aip_offices_aip_record_id_office_id",
                table: "aip_offices");

            migrationBuilder.DropIndex(
                name: "IX_aip_offices_office_id",
                table: "aip_offices");

            migrationBuilder.DropColumn(
                name: "office_id",
                table: "aip_offices");
        }
    }
}
