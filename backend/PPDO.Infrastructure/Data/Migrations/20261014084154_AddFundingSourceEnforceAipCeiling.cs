using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundingSourceEnforceAipCeiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enforce_aip_ceiling",
                table: "funding_sources",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // PPDO-216 — the shared General Fund is always enforced. The code never reads its flag,
            // but the stored value should still tell the truth to anyone querying the table.
            migrationBuilder.Sql(
                "UPDATE funding_sources SET enforce_aip_ceiling = 1 WHERE code = 'GF' AND office_id IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enforce_aip_ceiling",
                table: "funding_sources");
        }
    }
}
