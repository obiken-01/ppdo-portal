using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundingSourceToAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_wfp_division_allocation_ledger_division_fy_wfp_record",
                table: "wfp_division_allocation_ledger");

            migrationBuilder.DropIndex(
                name: "IX_division_allocations_div_fy",
                table: "division_allocations");

            migrationBuilder.DropIndex(
                name: "IX_budget_ceilings_office_fy",
                table: "budget_ceilings");

            migrationBuilder.AddColumn<int>(
                name: "funding_source_id",
                table: "wfp_division_allocation_ledger",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "funding_source_id",
                table: "division_allocations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "funding_source_id",
                table: "budget_ceilings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Backfill: every existing ceiling/allocation/ledger row — and every existing
            // WFP expenditure/line with no fund source selected — predates the fund-source
            // dimension and behaved as General Fund. Resolve the GF row by Code and fail
            // loudly if it's missing, rather than silently leaving funding_source_id = 0
            // (which would violate the FK added below).
            migrationBuilder.Sql(@"
DECLARE @gfId INT = (SELECT TOP 1 id FROM funding_sources WHERE code = 'GF');
IF @gfId IS NULL
    THROW 50000, 'Cannot backfill funding_source_id: no funding_sources row with Code = ''GF'' exists. Seed the General Fund funding source before applying this migration.', 1;

UPDATE budget_ceilings SET funding_source_id = @gfId;
UPDATE division_allocations SET funding_source_id = @gfId;
UPDATE wfp_division_allocation_ledger SET funding_source_id = @gfId;
UPDATE wfp_expenditures SET funding_source_id = @gfId, funding_source_snapshot = 'GF', funding_source_name_snapshot = 'General Fund' WHERE funding_source_id IS NULL;
UPDATE wfp_expenditure_lines SET funding_source_id = @gfId, funding_source_snapshot = 'GF', funding_source_name_snapshot = 'General Fund' WHERE funding_source_id IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_division_allocation_ledger_division_fy_fund_wfp_record",
                table: "wfp_division_allocation_ledger",
                columns: new[] { "division_id", "fiscal_year", "funding_source_id", "wfp_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wfp_division_allocation_ledger_funding_source_id",
                table: "wfp_division_allocation_ledger",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_division_allocations_div_fy_fund",
                table: "division_allocations",
                columns: new[] { "division_id", "fiscal_year", "funding_source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_division_allocations_funding_source_id",
                table: "division_allocations",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_ceilings_funding_source_id",
                table: "budget_ceilings",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_budget_ceilings_office_fy_fund",
                table: "budget_ceilings",
                columns: new[] { "office_id", "fiscal_year", "funding_source_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_budget_ceilings_funding_sources_funding_source_id",
                table: "budget_ceilings",
                column: "funding_source_id",
                principalTable: "funding_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_division_allocations_funding_sources_funding_source_id",
                table: "division_allocations",
                column: "funding_source_id",
                principalTable: "funding_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_wfp_division_allocation_ledger_funding_sources_funding_source_id",
                table: "wfp_division_allocation_ledger",
                column: "funding_source_id",
                principalTable: "funding_sources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_budget_ceilings_funding_sources_funding_source_id",
                table: "budget_ceilings");

            migrationBuilder.DropForeignKey(
                name: "FK_division_allocations_funding_sources_funding_source_id",
                table: "division_allocations");

            migrationBuilder.DropForeignKey(
                name: "FK_wfp_division_allocation_ledger_funding_sources_funding_source_id",
                table: "wfp_division_allocation_ledger");

            migrationBuilder.DropIndex(
                name: "IX_wfp_division_allocation_ledger_division_fy_fund_wfp_record",
                table: "wfp_division_allocation_ledger");

            migrationBuilder.DropIndex(
                name: "IX_wfp_division_allocation_ledger_funding_source_id",
                table: "wfp_division_allocation_ledger");

            migrationBuilder.DropIndex(
                name: "IX_division_allocations_div_fy_fund",
                table: "division_allocations");

            migrationBuilder.DropIndex(
                name: "IX_division_allocations_funding_source_id",
                table: "division_allocations");

            migrationBuilder.DropIndex(
                name: "IX_budget_ceilings_funding_source_id",
                table: "budget_ceilings");

            migrationBuilder.DropIndex(
                name: "IX_budget_ceilings_office_fy_fund",
                table: "budget_ceilings");

            migrationBuilder.DropColumn(
                name: "funding_source_id",
                table: "wfp_division_allocation_ledger");

            migrationBuilder.DropColumn(
                name: "funding_source_id",
                table: "division_allocations");

            migrationBuilder.DropColumn(
                name: "funding_source_id",
                table: "budget_ceilings");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_division_allocation_ledger_division_fy_wfp_record",
                table: "wfp_division_allocation_ledger",
                columns: new[] { "division_id", "fiscal_year", "wfp_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_division_allocations_div_fy",
                table: "division_allocations",
                columns: new[] { "division_id", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_budget_ceilings_office_fy",
                table: "budget_ceilings",
                columns: new[] { "office_id", "fiscal_year" },
                unique: true);
        }
    }
}
