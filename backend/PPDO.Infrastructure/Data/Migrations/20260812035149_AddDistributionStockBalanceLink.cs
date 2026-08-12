using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDistributionStockBalanceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "DeliveryItemId",
                table: "Distributions",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "StockBalanceId",
                table: "Distributions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Distributions_StockBalanceId",
                table: "Distributions",
                column: "StockBalanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Distributions_stock_balances_StockBalanceId",
                table: "Distributions",
                column: "StockBalanceId",
                principalTable: "stock_balances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Distributions_stock_balances_StockBalanceId",
                table: "Distributions");

            migrationBuilder.DropIndex(
                name: "IX_Distributions_StockBalanceId",
                table: "Distributions");

            migrationBuilder.DropColumn(
                name: "StockBalanceId",
                table: "Distributions");

            migrationBuilder.AlterColumn<Guid>(
                name: "DeliveryItemId",
                table: "Distributions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
