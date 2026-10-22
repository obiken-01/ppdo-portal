using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source_id",
                table: "aip_projects",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_id",
                table: "aip_programs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_id",
                table: "aip_procurement_items",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "saip_id",
                table: "aip_offices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_id",
                table: "aip_offices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_id",
                table: "aip_expenditures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_id",
                table: "aip_activities",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "aip_saips",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    aip_record_id = table.Column<int>(type: "int", nullable: false),
                    office_id = table.Column<int>(type: "int", nullable: false),
                    number = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                    letter_file_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    letter_file_name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    letter_content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    letter_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    started_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    decided_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_saips", x => x.id);
                    table.CheckConstraint("CK_aip_saips_status", "[status] IN ('Draft', 'DepartmentReview', 'SubmittedToPpdo', 'ReturnedByPpdo', 'Approved', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_aip_saips_aip_records_aip_record_id",
                        column: x => x.aip_record_id,
                        principalTable: "aip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_saips_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_saips_users_cancelled_by_id",
                        column: x => x.cancelled_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_saips_users_decided_by_id",
                        column: x => x.decided_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_saips_users_started_by_id",
                        column: x => x.started_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_saips_users_submitted_by_id",
                        column: x => x.submitted_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aip_saip_changes",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    saip_id = table.Column<int>(type: "int", nullable: false),
                    node_type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    node_id = table.Column<int>(type: "int", nullable: false),
                    change = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    before = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_saip_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_saip_changes_aip_saips_saip_id",
                        column: x => x.saip_id,
                        principalTable: "aip_saips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_saip_id",
                table: "aip_offices",
                column: "saip_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_saip_changes_saip_id",
                table: "aip_saip_changes",
                column: "saip_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_saips_cancelled_by_id",
                table: "aip_saips",
                column: "cancelled_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_saips_decided_by_id",
                table: "aip_saips",
                column: "decided_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_saips_office_id",
                table: "aip_saips",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_saips_started_by_id",
                table: "aip_saips",
                column: "started_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_saips_submitted_by_id",
                table: "aip_saips",
                column: "submitted_by_id");

            migrationBuilder.CreateIndex(
                name: "UX_aip_saips_one_open_per_office",
                table: "aip_saips",
                columns: new[] { "aip_record_id", "office_id" },
                unique: true,
                filter: "[status] <> 'Approved' AND [status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "UX_aip_saips_record_office_number",
                table: "aip_saips",
                columns: new[] { "aip_record_id", "office_id", "number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_aip_offices_aip_saips_saip_id",
                table: "aip_offices",
                column: "saip_id",
                principalTable: "aip_saips",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_aip_offices_aip_saips_saip_id",
                table: "aip_offices");

            migrationBuilder.DropTable(
                name: "aip_saip_changes");

            migrationBuilder.DropTable(
                name: "aip_saips");

            migrationBuilder.DropIndex(
                name: "IX_aip_offices_saip_id",
                table: "aip_offices");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "aip_projects");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "aip_programs");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "aip_procurement_items");

            migrationBuilder.DropColumn(
                name: "saip_id",
                table: "aip_offices");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "aip_offices");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "aip_expenditures");

            migrationBuilder.DropColumn(
                name: "source_id",
                table: "aip_activities");
        }
    }
}
