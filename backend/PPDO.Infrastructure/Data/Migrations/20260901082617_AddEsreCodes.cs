using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEsreCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "esre_codes",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_esre_codes", x => x.id);
                });

            // The eSRE vocabulary is a CLOSED list of four, so it is seeded literally here rather than
            // derived from aip_activities the way the climate-change typologies are. Deriving would also
            // pick up the one FY2027 row reading "PPDO/PEO" -- an implementing-office name typed into the
            // eSRE column (AIP_Form_Spec.md 3.1) -- and legitimise the exact typo this table exists to
            // make impossible. That row stays orphaned for the Phase 2 backfill to flag.
            //
            // Guarded by NOT EXISTS so re-running against an environment that already has the codes is a
            // no-op rather than a unique-index violation.
            migrationBuilder.Sql(@"
                INSERT INTO esre_codes (code, name, is_active, created_at, updated_at)
                SELECT v.code, v.name, 1, GETUTCDATE(), GETUTCDATE()
                FROM (VALUES
                    ('SS', 'Social Services'),
                    ('ES', 'Economic Services'),
                    ('ID', 'Institutional Development'),
                    ('EN', 'Environmental Services')
                ) AS v(code, name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM esre_codes AS e WHERE e.code = v.code
                );");

            migrationBuilder.CreateIndex(
                name: "IX_esre_codes_code",
                table: "esre_codes",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "esre_codes");
        }
    }
}
