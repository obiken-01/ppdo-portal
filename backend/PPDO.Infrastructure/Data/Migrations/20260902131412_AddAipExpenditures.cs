using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipExpenditures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aip_expenditures",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    activity_id = table.Column<int>(type: "int", nullable: false),
                    account_id = table.Column<int>(type: "int", nullable: true),
                    account_number_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    account_title_snapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    funding_source_id = table.Column<int>(type: "int", nullable: true),
                    funding_source_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    funding_source_name_snapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ps = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    mooe = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    co = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    total = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_expenditures", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_expenditures_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_expenditures_aip_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "aip_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_aip_expenditures_funding_sources_funding_source_id",
                        column: x => x.funding_source_id,
                        principalTable: "funding_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aip_expenditures_account_id",
                table: "aip_expenditures",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_expenditures_activity_id",
                table: "aip_expenditures",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_expenditures_funding_source_id",
                table: "aip_expenditures",
                column: "funding_source_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aip_expenditures");
        }
    }
}
