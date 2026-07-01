using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipProgramProjectLineItemFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "cc_adaptation",
                table: "aip_projects",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cc_mitigation",
                table: "aip_projects",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cc_typology_code",
                table: "aip_projects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "co",
                table: "aip_projects",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "end_date",
                table: "aip_projects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "esre_code",
                table: "aip_projects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_outputs",
                table: "aip_projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "funding_source_id",
                table: "aip_projects",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "funding_source_snapshot",
                table: "aip_projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "implementing_office",
                table: "aip_projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "mooe",
                table: "aip_projects",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ps",
                table: "aip_projects",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "start_date",
                table: "aip_projects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "aip_projects",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cc_adaptation",
                table: "aip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cc_mitigation",
                table: "aip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cc_typology_code",
                table: "aip_programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "co",
                table: "aip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "end_date",
                table: "aip_programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "esre_code",
                table: "aip_programs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_outputs",
                table: "aip_programs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "funding_source_id",
                table: "aip_programs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "funding_source_snapshot",
                table: "aip_programs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "implementing_office",
                table: "aip_programs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "mooe",
                table: "aip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ps",
                table: "aip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "start_date",
                table: "aip_programs",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total",
                table: "aip_programs",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_projects_funding_source_id",
                table: "aip_projects",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_programs_funding_source_id",
                table: "aip_programs",
                column: "funding_source_id");

            migrationBuilder.AddForeignKey(
                name: "FK_aip_programs_funding_sources_funding_source_id",
                table: "aip_programs",
                column: "funding_source_id",
                principalTable: "funding_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_aip_projects_funding_sources_funding_source_id",
                table: "aip_projects",
                column: "funding_source_id",
                principalTable: "funding_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_programs_funding_sources_funding_source_id",
                table: "aip_programs");

            migrationBuilder.DropForeignKey(
                name: "FK_aip_projects_funding_sources_funding_source_id",
                table: "aip_projects");

            migrationBuilder.DropIndex(
                name: "IX_aip_projects_funding_source_id",
                table: "aip_projects");

            migrationBuilder.DropIndex(
                name: "IX_aip_programs_funding_source_id",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "cc_adaptation",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "cc_mitigation",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "cc_typology_code",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "co",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "esre_code",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "expected_outputs",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "funding_source_id",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "funding_source_snapshot",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "implementing_office",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "mooe",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "ps",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "total",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "cc_adaptation",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "cc_mitigation",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "cc_typology_code",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "co",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "esre_code",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "expected_outputs",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "funding_source_id",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "funding_source_snapshot",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "implementing_office",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "mooe",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "ps",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "start_date",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "total",
                table: "aip_programs");
        }
    }
}
