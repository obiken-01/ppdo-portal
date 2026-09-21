using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Per-office fund sources (v1.8.0 — PPDO-109, D5). Adds a nullable <c>office_id</c> to
    /// <c>funding_sources</c>: null means province-wide and PPDO-owned, a value means the fund
    /// belongs to that office alone.
    ///
    /// ⚠️ <b>Deliberately no backfill.</b> Every existing row keeps <c>office_id = NULL</c>, which
    /// is exactly today's behaviour — the whole table stays shared and nothing changes for anyone
    /// until a department head adds a fund of their own. A backfill is what would have been needed
    /// by the per-office-copies model D5 rejected.
    ///
    /// ⚠️ <c>IX_funding_sources_code</c> is untouched: codes stay globally unique (D6).
    ///
    /// CI does not run migrations — apply by hand to Azure SQL at release.
    /// </summary>
    public partial class AddFundingSourceOfficeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "office_id",
                table: "funding_sources",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_funding_sources_office_id",
                table: "funding_sources",
                column: "office_id");

            migrationBuilder.AddForeignKey(
                name: "FK_funding_sources_offices_office_id",
                table: "funding_sources",
                column: "office_id",
                principalTable: "offices",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_funding_sources_offices_office_id",
                table: "funding_sources");

            migrationBuilder.DropIndex(
                name: "IX_funding_sources_office_id",
                table: "funding_sources");

            migrationBuilder.DropColumn(
                name: "office_id",
                table: "funding_sources");
        }
    }
}
