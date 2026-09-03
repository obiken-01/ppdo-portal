using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// V18-35 (PPDO-34) — AIP money moves from thousands (₱000) to PESOS, for EVERY fiscal year.
    ///
    /// <para>
    /// DATA ONLY. No column, type or constraint changes — <c>decimal(18,2)</c> already holds the
    /// larger values with room to spare, so there is nothing for EF to diff and the Up/Down bodies
    /// are hand-written SQL.
    /// </para>
    ///
    /// <para>
    /// DECISION E (2026-08-25): units migrate across all years even though the record SHAPE
    /// partitions at FY2028. Under a partition <c>aip_activities.total</c> would stop being
    /// readable without knowing the fiscal year — permanently — and the six ×1000 conversion sites
    /// would become six FY-conditional branches that must stay correct forever. Getting one wrong
    /// in the permissive direction is silent: the WFP ceiling simply never trips again. Migrating
    /// deletes that failure mode; partitioning would have scheduled it.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Reversible and verifiable, which is why it was safe to do at all. <c>Down</c> divides by
    /// the same factor. Verify with the ratio, not by eye — per fiscal year, before and after:
    /// </para>
    /// <code>
    /// SELECT r.fiscal_year, SUM(a.total)
    /// FROM   aip_activities a
    ///        JOIN aip_projects  p ON p.id  = a.project_id
    ///        JOIN aip_programs  g ON g.id  = p.program_id
    ///        JOIN aip_offices   o ON o.id  = g.office_id
    ///        JOIN aip_records   r ON r.id  = o.aip_record_id
    /// GROUP  BY r.fiscal_year;
    /// </code>
    /// <para>Every year's sum must come out exactly ×1000. Back the database up first.</para>
    ///
    /// <para>
    /// NULLs stay NULL: in SQL Server <c>NULL * 1000</c> is NULL, which is the wanted behaviour —
    /// an uncosted activity has no amount, and must not become 0.
    /// </para>
    /// </summary>
    public partial class MigrateAipAmountsToPesos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE aip_activities SET
                    ps            = ps            * 1000,
                    mooe          = mooe          * 1000,
                    co            = co            * 1000,
                    total         = total         * 1000,
                    cc_adaptation = cc_adaptation * 1000,
                    cc_mitigation = cc_mitigation * 1000;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE aip_activities SET
                    ps            = ps            / 1000,
                    mooe          = mooe          / 1000,
                    co            = co            / 1000,
                    total         = total         / 1000,
                    cc_adaptation = cc_adaptation / 1000,
                    cc_mitigation = cc_mitigation / 1000;
                """);
        }
    }
}
