using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// V18-40 (PPDO-39) — an AIP record can belong to one office.
    ///
    /// <para>
    /// Purely additive: a nullable column, an index and an FK. <b>There is deliberately no
    /// backfill</b>, which is the difference from <c>AddAipOfficeOwnershipFk</c>. A legacy record
    /// spans every office in the province, so it has no single owner to fill in — null is its
    /// permanent, correct value, not a gap awaiting resolution. FY≤2027 records keep the old shape
    /// untouched and there is no conversion between the two (V18-37).
    /// </para>
    /// </summary>
    public partial class AddAipRecordOwningOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "office_id",
                table: "aip_records",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_records_office_id_fiscal_year",
                table: "aip_records",
                columns: new[] { "office_id", "fiscal_year" });

            migrationBuilder.AddForeignKey(
                name: "FK_aip_records_offices_office_id",
                table: "aip_records",
                column: "office_id",
                principalTable: "offices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_records_offices_office_id",
                table: "aip_records");

            migrationBuilder.DropIndex(
                name: "IX_aip_records_office_id_fiscal_year",
                table: "aip_records");

            migrationBuilder.DropColumn(
                name: "office_id",
                table: "aip_records");
        }
    }
}
