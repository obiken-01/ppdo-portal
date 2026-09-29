using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Seeds the final CC typology list (PPDO-139) — the LGU typology, JMC 2015-01 (DBM-CCC-DILG)
    /// Annex A, plus the FY2027 AIP codes that are not in it, the latter inactive.
    ///
    /// ⚠️ Not JMC 2013-01: that national-agency list reuses the same code numbers with different
    /// meanings, and the province's AIP is tagged against the LGU list (56 of its 60 codes).
    ///
    /// Merges by code rather than inserting: <c>AddClimateChangeTypologies</c> already seeded every
    /// code in the FY2027 AIP (name = the code), and UAT has had the config page since. Rows in the
    /// table but not in the list are deactivated and their name reset to the code, never deleted —
    /// an activity may still carry them, and must not show a title the LGU list does not give it.
    ///
    /// The SQL lives in the generated <c>.Data.cs</c> partial: edit
    /// <c>docs/v1.8/seed/cc_typologies_seed.csv</c> and run <c>scripts/cc_typologies_seed.py</c>.
    /// </summary>
    public partial class SeedClimateChangeTypologies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MergeSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. The names and descriptions this overwrote were placeholders
            // (the code itself) or config-page edits, and there is no record of them to restore.
            // Rolling back past AddClimateChangeTypologies drops the table anyway.
        }
    }
}
