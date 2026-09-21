using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipProcurementItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aip_procurement_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    expenditure_id = table.Column<int>(type: "int", nullable: false),
                    price_index_item_id = table.Column<int>(type: "int", nullable: true),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    number_of_days = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 1m),
                    line_total = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_procurement_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_procurement_items_aip_expenditures_expenditure_id",
                        column: x => x.expenditure_id,
                        principalTable: "aip_expenditures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_aip_procurement_items_price_index_items_price_index_item_id",
                        column: x => x.price_index_item_id,
                        principalTable: "price_index_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aip_procurement_items_expenditure_id",
                table: "aip_procurement_items",
                column: "expenditure_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_procurement_items_price_index_item_id",
                table: "aip_procurement_items",
                column: "price_index_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aip_procurement_items");
        }
    }
}
