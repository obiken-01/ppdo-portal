using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentPlanningSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "investment_planning_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    default_fiscal_year = table.Column<int>(type: "int", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_investment_planning_settings", x => x.id);
                    table.CheckConstraint("CK_investment_planning_settings_singleton", "[id] = 1");
                    table.ForeignKey(
                        name: "FK_investment_planning_settings_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "investment_planning_settings",
                columns: new[] { "id", "default_fiscal_year", "updated_at", "updated_by_user_id" },
                values: new object[] { 1, null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_investment_planning_settings_updated_by_user_id",
                table: "investment_planning_settings",
                column: "updated_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "investment_planning_settings");
        }
    }
}
