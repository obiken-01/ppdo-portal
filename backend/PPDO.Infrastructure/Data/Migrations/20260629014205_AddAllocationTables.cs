using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_ceilings",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    office_id = table.Column<int>(type: "int", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_ceilings", x => x.id);
                    table.ForeignKey(
                        name: "FK_budget_ceilings_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "division_allocations",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    division_id = table.Column<int>(type: "int", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_division_allocations", x => x.id);
                    table.ForeignKey(
                        name: "FK_division_allocations_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "program_divisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    office_ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    program_ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    division_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_program_divisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_program_divisions_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_budget_ceilings_office_fy",
                table: "budget_ceilings",
                columns: new[] { "office_id", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_division_allocations_div_fy",
                table: "division_allocations",
                columns: new[] { "division_id", "fiscal_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_program_divisions_division_id",
                table: "program_divisions",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "IX_program_divisions_ref_div",
                table: "program_divisions",
                columns: new[] { "office_ref_code", "program_ref_code", "division_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_ceilings");

            migrationBuilder.DropTable(
                name: "division_allocations");

            migrationBuilder.DropTable(
                name: "program_divisions");
        }
    }
}
