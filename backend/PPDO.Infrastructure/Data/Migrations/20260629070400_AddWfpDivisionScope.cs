using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWfpDivisionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_wfp_records_aip_record_id_office_id",
                table: "wfp_records");

            migrationBuilder.AddColumn<int>(
                name: "division_id",
                table: "wfp_records",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wfp_records_division_id",
                table: "wfp_records",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "UX_wfp_records_aip_office_division",
                table: "wfp_records",
                columns: new[] { "aip_record_id", "office_id", "division_id" },
                unique: true,
                filter: "[division_id] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_wfp_records_divisions_division_id",
                table: "wfp_records",
                column: "division_id",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wfp_records_divisions_division_id",
                table: "wfp_records");

            migrationBuilder.DropIndex(
                name: "IX_wfp_records_division_id",
                table: "wfp_records");

            migrationBuilder.DropIndex(
                name: "UX_wfp_records_aip_office_division",
                table: "wfp_records");

            migrationBuilder.DropColumn(
                name: "division_id",
                table: "wfp_records");

            migrationBuilder.CreateIndex(
                name: "UX_wfp_records_aip_record_id_office_id",
                table: "wfp_records",
                columns: new[] { "aip_record_id", "office_id" },
                unique: true);
        }
    }
}
