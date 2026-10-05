using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PriceIndexUniqueByStockCardNo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_price_index_items_name_unit",
                table: "price_index_items");

            migrationBuilder.CreateIndex(
                name: "IX_price_index_items_name_unit_stock_card_no",
                table: "price_index_items",
                columns: new[] { "name", "unit", "stock_card_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_price_index_items_name_unit_stock_card_no",
                table: "price_index_items");

            migrationBuilder.CreateIndex(
                name: "IX_price_index_items_name_unit",
                table: "price_index_items",
                columns: new[] { "name", "unit" },
                unique: true);
        }
    }
}
