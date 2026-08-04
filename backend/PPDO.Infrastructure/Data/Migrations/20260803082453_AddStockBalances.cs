using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    stock_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    counted_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    system_on_hand_at_entry = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    variance_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balances", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_stock_no",
                table: "stock_balances",
                column: "stock_no");

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_stock_no_effective_date",
                table: "stock_balances",
                columns: new[] { "stock_no", "effective_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_balances");
        }
    }
}
