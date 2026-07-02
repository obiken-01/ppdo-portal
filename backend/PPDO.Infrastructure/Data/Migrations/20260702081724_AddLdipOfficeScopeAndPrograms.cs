using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLdipOfficeScopeAndPrograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "office_id",
                table: "ldip_records",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ldip_offices",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ldip_record_id = table.Column<int>(type: "int", nullable: false),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    sector = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ldip_offices", x => x.id);
                    table.ForeignKey(
                        name: "FK_ldip_offices_ldip_records_ldip_record_id",
                        column: x => x.ldip_record_id,
                        principalTable: "ldip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ldip_programs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ldip_office_id = table.Column<int>(type: "int", nullable: false),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    budget = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ldip_programs", x => x.id);
                    table.ForeignKey(
                        name: "FK_ldip_programs_ldip_offices_ldip_office_id",
                        column: x => x.ldip_office_id,
                        principalTable: "ldip_offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ldip_records_office_id",
                table: "ldip_records",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_ldip_offices_ldip_record_id",
                table: "ldip_offices",
                column: "ldip_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_ldip_offices_ldip_record_id_ref_code",
                table: "ldip_offices",
                columns: new[] { "ldip_record_id", "ref_code" });

            migrationBuilder.CreateIndex(
                name: "IX_ldip_programs_ldip_office_id",
                table: "ldip_programs",
                column: "ldip_office_id");

            migrationBuilder.CreateIndex(
                name: "UX_ldip_programs_ldip_office_id_ref_code",
                table: "ldip_programs",
                columns: new[] { "ldip_office_id", "ref_code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ldip_records_offices_office_id",
                table: "ldip_records",
                column: "office_id",
                principalTable: "offices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ldip_records_offices_office_id",
                table: "ldip_records");

            migrationBuilder.DropTable(
                name: "ldip_programs");

            migrationBuilder.DropTable(
                name: "ldip_offices");

            migrationBuilder.DropIndex(
                name: "IX_ldip_records_office_id",
                table: "ldip_records");

            migrationBuilder.DropColumn(
                name: "office_id",
                table: "ldip_records");
        }
    }
}
