using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipDivisionAllocationLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aip_division_allocation_ledger",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    division_id = table.Column<int>(type: "int", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    funding_source_id = table.Column<int>(type: "int", nullable: false),
                    aip_activity_id = table.Column<int>(type: "int", nullable: false),
                    allocated_amount_snapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    reserved_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_division_allocation_ledger", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_division_allocation_ledger_aip_activities_aip_activity_id",
                        column: x => x.aip_activity_id,
                        principalTable: "aip_activities",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_aip_division_allocation_ledger_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_division_allocation_ledger_funding_sources_funding_source_id",
                        column: x => x.funding_source_id,
                        principalTable: "funding_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_allocation_ledger_aip_activity_id",
                table: "aip_division_allocation_ledger",
                column: "aip_activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_allocation_ledger_division_fy_fund_activity",
                table: "aip_division_allocation_ledger",
                columns: new[] { "division_id", "fiscal_year", "funding_source_id", "aip_activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_division_allocation_ledger_funding_source_id",
                table: "aip_division_allocation_ledger",
                column: "funding_source_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aip_division_allocation_ledger");
        }
    }
}
