using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClimateChangeTypologies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "climate_change_typologies",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_climate_change_typologies", x => x.id);
                });

            // Seed the vocabulary from the codes the province actually uses, read out of this same
            // database rather than hardcoded here: the FY2027 AIP import is the only authoritative
            // source for them, and it differs per environment.
            //
            // Split on BOTH separators. 18 of the 167 tagged FY2027 activities hold two codes in the
            // one free-text field, and the field uses a comma in some rows and a semicolon in others
            // ("A222-03, A224-05" and "A123-01; A314-08"). Splitting on the comma alone seeds the
            // semicolon pairs as single bogus codes - found by reading the seeded rows back, not by
            // reading the data first, which is why this comment exists.
            //
            // Name is seeded to the code itself: the codes exist in the data, the province's official
            // descriptions for them do not. Category comes from the CCET leading letter; anything else
            // is filed 'Unclassified' rather than guessed, so it stands out on the page.
            migrationBuilder.Sql(@"
                INSERT INTO climate_change_typologies (code, name, category, is_active, created_at, updated_at)
                SELECT DISTINCT c.code, c.code,
                       CASE LEFT(c.code, 1) WHEN 'A' THEN 'Adaptation'
                                            WHEN 'M' THEN 'Mitigation'
                                            ELSE 'Unclassified' END,
                       1, GETUTCDATE(), GETUTCDATE()
                FROM (
                    SELECT UPPER(LTRIM(RTRIM(s.value))) AS code
                    FROM   aip_activities AS a
                    CROSS APPLY STRING_SPLIT(REPLACE(a.cc_typology_code, ';', ','), ',') AS s
                    WHERE  a.cc_typology_code IS NOT NULL
                      AND  LTRIM(RTRIM(s.value)) <> ''
                      AND  LEN(LTRIM(RTRIM(s.value))) <= 20
                ) AS c
                WHERE NOT EXISTS (
                    SELECT 1 FROM climate_change_typologies AS t WHERE t.code = c.code
                );");

            migrationBuilder.CreateIndex(
                name: "IX_climate_change_typologies_code",
                table: "climate_change_typologies",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "climate_change_typologies");
        }
    }
}
