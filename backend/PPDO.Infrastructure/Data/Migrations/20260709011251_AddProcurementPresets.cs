using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcurementPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "procurement_presets",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    account_id = table.Column<int>(type: "int", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_presets", x => x.id);
                    table.ForeignKey(
                        name: "FK_procurement_presets_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_procurement_presets_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procurement_preset_items",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    preset_id = table.Column<int>(type: "int", nullable: false),
                    price_index_item_id = table.Column<int>(type: "int", nullable: true),
                    name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    unit_price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    default_qty = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_procurement_preset_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_procurement_preset_items_price_index_items_price_index_item_id",
                        column: x => x.price_index_item_id,
                        principalTable: "price_index_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_procurement_preset_items_procurement_presets_preset_id",
                        column: x => x.preset_id,
                        principalTable: "procurement_presets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_procurement_preset_items_preset_id",
                table: "procurement_preset_items",
                column: "preset_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_preset_items_price_index_item_id",
                table: "procurement_preset_items",
                column: "price_index_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_presets_account_id",
                table: "procurement_presets",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_procurement_presets_created_by_id",
                table: "procurement_presets",
                column: "created_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "procurement_preset_items");

            migrationBuilder.DropTable(
                name: "procurement_presets");
        }
    }
}
