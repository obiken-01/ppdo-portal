using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWfpDivisionAllocationLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wfp_division_allocation_ledger",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    division_id = table.Column<int>(type: "int", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    wfp_record_id = table.Column<int>(type: "int", nullable: false),
                    allocated_amount_snapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    used_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_division_allocation_ledger", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_division_allocation_ledger_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_division_allocation_ledger_wfp_records_wfp_record_id",
                        column: x => x.wfp_record_id,
                        principalTable: "wfp_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wfp_division_allocation_ledger_division_fy_wfp_record",
                table: "wfp_division_allocation_ledger",
                columns: new[] { "division_id", "fiscal_year", "wfp_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wfp_division_allocation_ledger_wfp_record_id",
                table: "wfp_division_allocation_ledger",
                column: "wfp_record_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wfp_division_allocation_ledger");
        }
    }
}
