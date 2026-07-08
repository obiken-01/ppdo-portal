using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWfpExpenditureSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wfp_expenditures",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wfp_activity_id = table.Column<int>(type: "int", nullable: false),
                    account_id = table.Column<int>(type: "int", nullable: true),
                    account_number_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    account_title_snapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    nature = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    funding_source_id = table.Column<int>(type: "int", nullable: true),
                    funding_source_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    funding_source_name_snapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    apply_reserve = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    reserve_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    annual_quarter_choice = table.Column<int>(type: "int", nullable: true),
                    q1 = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    q2 = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    q3 = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    q4 = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    net_appropriation = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    total_appropriation = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_expenditures", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_expenditures_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_expenditures_funding_sources_funding_source_id",
                        column: x => x.funding_source_id,
                        principalTable: "funding_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_expenditures_wfp_activities_wfp_activity_id",
                        column: x => x.wfp_activity_id,
                        principalTable: "wfp_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wfp_expenditure_periods",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    expenditure_id = table.Column<int>(type: "int", nullable: false),
                    period_no = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_expenditure_periods", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_expenditure_periods_wfp_expenditures_expenditure_id",
                        column: x => x.expenditure_id,
                        principalTable: "wfp_expenditures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wfp_procurement_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    expenditure_id = table.Column<int>(type: "int", nullable: false),
                    period_no = table.Column<int>(type: "int", nullable: false),
                    price_index_item_id = table.Column<int>(type: "int", nullable: true),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    line_total = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_procurement_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_procurement_items_price_index_items_price_index_item_id",
                        column: x => x.price_index_item_id,
                        principalTable: "price_index_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_procurement_items_wfp_expenditures_expenditure_id",
                        column: x => x.expenditure_id,
                        principalTable: "wfp_expenditures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wfp_expenditure_periods_expenditure_id_period_no",
                table: "wfp_expenditure_periods",
                columns: new[] { "expenditure_id", "period_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wfp_expenditures_account_id",
                table: "wfp_expenditures",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_expenditures_funding_source_id",
                table: "wfp_expenditures",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_expenditures_wfp_activity_id",
                table: "wfp_expenditures",
                column: "wfp_activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_procurement_items_expenditure_id",
                table: "wfp_procurement_items",
                column: "expenditure_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_procurement_items_price_index_item_id",
                table: "wfp_procurement_items",
                column: "price_index_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wfp_expenditure_periods");

            migrationBuilder.DropTable(
                name: "wfp_procurement_items");

            migrationBuilder.DropTable(
                name: "wfp_expenditures");
        }
    }
}
