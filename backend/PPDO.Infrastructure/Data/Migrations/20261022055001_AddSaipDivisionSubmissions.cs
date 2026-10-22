using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaipDivisionSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_aip_division_submissions_record_division",
                table: "aip_division_submissions");

            migrationBuilder.AddColumn<int>(
                name: "saip_id",
                table: "aip_division_submissions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_submissions_saip_id",
                table: "aip_division_submissions",
                column: "saip_id");

            migrationBuilder.CreateIndex(
                name: "UX_aip_division_submissions_record_division_saip",
                table: "aip_division_submissions",
                columns: new[] { "aip_record_id", "division_id", "saip_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_aip_division_submissions_aip_saips_saip_id",
                table: "aip_division_submissions",
                column: "saip_id",
                principalTable: "aip_saips",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_division_submissions_aip_saips_saip_id",
                table: "aip_division_submissions");

            migrationBuilder.DropIndex(
                name: "IX_aip_division_submissions_saip_id",
                table: "aip_division_submissions");

            migrationBuilder.DropIndex(
                name: "UX_aip_division_submissions_record_division_saip",
                table: "aip_division_submissions");

            migrationBuilder.DropColumn(
                name: "saip_id",
                table: "aip_division_submissions");

            migrationBuilder.CreateIndex(
                name: "UX_aip_division_submissions_record_division",
                table: "aip_division_submissions",
                columns: new[] { "aip_record_id", "division_id" },
                unique: true);
        }
    }
}
