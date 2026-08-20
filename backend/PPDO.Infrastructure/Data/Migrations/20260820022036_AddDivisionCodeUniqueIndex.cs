using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDivisionCodeUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Blank codes are semantically "no code" (the app writes NULL via NullIfBlank), but
            // legacy rows may hold ''. Two of those would collide under the filtered index, so
            // normalise them to NULL first.
            migrationBuilder.Sql(
                "UPDATE divisions SET code = NULL WHERE code IS NOT NULL AND LTRIM(RTRIM(code)) = '';");

            // Code becomes the CSV upsert key (RAL-239), so duplicates within an office are no
            // longer tolerable. Fail with the offending rows named rather than letting
            // CREATE UNIQUE INDEX report a bare duplicate-key error.
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM divisions
    WHERE code IS NOT NULL
    GROUP BY office_id, LOWER(code)
    HAVING COUNT(*) > 1)
BEGIN
    DECLARE @dupes NVARCHAR(MAX);
    SELECT @dupes = STRING_AGG(CONCAT('office_id=', office_id, ' code=', code), '; ')
    FROM (
        SELECT office_id, MIN(code) AS code
        FROM divisions
        WHERE code IS NOT NULL
        GROUP BY office_id, LOWER(code)
        HAVING COUNT(*) > 1
    ) AS d;

    DECLARE @msg NVARCHAR(MAX) = CONCAT(
        'RAL-239: cannot create IX_divisions_office_id_code - duplicate division codes exist within an office. ',
        'Give each division a distinct code (or clear it) and re-run this migration. Offending: ', @dupes);
    THROW 50000, @msg, 1;
END;");

            migrationBuilder.CreateIndex(
                name: "IX_divisions_office_id_code",
                table: "divisions",
                columns: new[] { "office_id", "code" },
                unique: true,
                filter: "[code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_divisions_office_id_code",
                table: "divisions");
        }
    }
}
