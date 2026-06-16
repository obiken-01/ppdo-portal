using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropAipOfficesUniqueRefCodeConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_aip_offices_aip_record_id_ref_code",
                table: "aip_offices");

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_aip_record_id_ref_code",
                table: "aip_offices",
                columns: new[] { "aip_record_id", "ref_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_aip_offices_aip_record_id_ref_code",
                table: "aip_offices");

            migrationBuilder.CreateIndex(
                name: "UX_aip_offices_aip_record_id_ref_code",
                table: "aip_offices",
                columns: new[] { "aip_record_id", "ref_code" },
                unique: true);
        }
    }
}
