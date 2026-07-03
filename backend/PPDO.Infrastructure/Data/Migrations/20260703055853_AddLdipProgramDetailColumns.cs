using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLdipProgramDetailColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cc_adaptation",
                table: "ldip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cc_mitigation",
                table: "ldip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cc_typology_code",
                table: "ldip_programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "co",
                table: "ldip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "end_date",
                table: "ldip_programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_outputs",
                table: "ldip_programs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "funding_source_id",
                table: "ldip_programs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "funding_source_snapshot",
                table: "ldip_programs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "implementing_office",
                table: "ldip_programs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "mooe",
                table: "ldip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ndrrm_plan",
                table: "ldip_programs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nsp",
                table: "ldip_programs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pdp_rdp",
                table: "ldip_programs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pdpdfp",
                table: "ldip_programs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ps",
                table: "ldip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sdgs",
                table: "ldip_programs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sendai_framework",
                table: "ldip_programs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "start_date",
                table: "ldip_programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ldip_programs_funding_source_id",
                table: "ldip_programs",
                column: "funding_source_id");

            migrationBuilder.AddForeignKey(
                name: "FK_ldip_programs_funding_sources_funding_source_id",
                table: "ldip_programs",
                column: "funding_source_id",
                principalTable: "funding_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ldip_programs_funding_sources_funding_source_id",
                table: "ldip_programs");

            migrationBuilder.DropIndex(
                name: "IX_ldip_programs_funding_source_id",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "cc_adaptation",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "cc_mitigation",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "cc_typology_code",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "co",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "expected_outputs",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "funding_source_id",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "funding_source_snapshot",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "implementing_office",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "mooe",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "ndrrm_plan",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "nsp",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "pdp_rdp",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "pdpdfp",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "ps",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "sdgs",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "sendai_framework",
                table: "ldip_programs");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "ldip_programs");
        }
    }
}
