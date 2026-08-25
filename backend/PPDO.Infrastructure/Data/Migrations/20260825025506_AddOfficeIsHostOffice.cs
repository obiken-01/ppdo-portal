using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficeIsHostOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_host_office",
                table: "offices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_offices_is_host_office",
                table: "offices",
                column: "is_host_office",
                unique: true,
                filter: "[is_host_office] = 1");

            // ── Backfill (DECISION F, RAL-258) ────────────────────────────────
            // Flag the PPDO row, then give every office-less user a real office_id. Order matters:
            // the users update reads the flag to find its target.
            //
            // "PPDO" appears here and nowhere else in the codebase any more. This is the one place
            // it belongs — a migration is a statement about the data as it stood on this date, not
            // a rule the application keeps re-evaluating.
            //
            // Cheap only because no production office accounts exist yet: every user is PPDO-
            // internal, so this is a single UPDATE with no ambiguous rows to resolve by hand.
            migrationBuilder.Sql("""
                UPDATE offices SET is_host_office = 1 WHERE office_code = 'PPDO';
                """);

            migrationBuilder.Sql("""
                DECLARE @hostOfficeId INT =
                    (SELECT TOP 1 id FROM offices WHERE is_host_office = 1);

                IF @hostOfficeId IS NOT NULL
                    UPDATE Users SET OfficeId = @hostOfficeId WHERE OfficeId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ⚠️ Asymmetric on purpose. Dropping the column restores the schema, but which users
            // had a null OfficeId before the backfill is not recoverable — that information is
            // gone the moment the UPDATE above runs. Rolling back leaves every user pointing at
            // the office they were assigned, which the old code reads as "a non-PPDO office user"
            // and would scope them all to PPDO's data only. Restore from a backup instead of
            // relying on this path if the backfill has already run against real data.
            migrationBuilder.DropIndex(
                name: "UX_offices_is_host_office",
                table: "offices");

            migrationBuilder.DropColumn(
                name: "is_host_office",
                table: "offices");
        }
    }
}
