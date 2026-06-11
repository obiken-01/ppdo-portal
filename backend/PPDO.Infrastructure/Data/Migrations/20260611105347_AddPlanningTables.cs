using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    account_title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    account_number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    normal_balance = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    table_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<int>(type: "int", nullable: false),
                    action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    changed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    changed_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    old_values = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    new_values = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_log_Users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "funding_sources",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_funding_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ldip_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    fiscal_year_start = table.Column<int>(type: "int", nullable: false),
                    fiscal_year_end = table.Column<int>(type: "int", nullable: false),
                    entry_mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    source_id = table.Column<int>(type: "int", nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ldip_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_ldip_records_Users_created_by",
                        column: x => x.created_by,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ldip_records_ldip_records_source_id",
                        column: x => x.source_id,
                        principalTable: "ldip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "offices",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    office_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    office_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_offices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "aip_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    entry_source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    original_filename = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    uploaded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    uploaded_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    ldip_id = table.Column<int>(type: "int", nullable: true),
                    source_id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_records_Users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_records_aip_records_source_id",
                        column: x => x.source_id,
                        principalTable: "aip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_aip_records_ldip_records_ldip_id",
                        column: x => x.ldip_id,
                        principalTable: "ldip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aip_offices",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    aip_record_id = table.Column<int>(type: "int", nullable: false),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    sector = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_offices", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_offices_aip_records_aip_record_id",
                        column: x => x.aip_record_id,
                        principalTable: "aip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wfp_records",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    aip_record_id = table.Column<int>(type: "int", nullable: false),
                    office_id = table.Column<int>(type: "int", nullable: false),
                    fiscal_year = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    finalized_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    source_id = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_records_Users_created_by",
                        column: x => x.created_by,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_records_aip_records_aip_record_id",
                        column: x => x.aip_record_id,
                        principalTable: "aip_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_records_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_records_wfp_records_source_id",
                        column: x => x.source_id,
                        principalTable: "wfp_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "aip_programs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    office_id = table.Column<int>(type: "int", nullable: false),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_programs", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_programs_aip_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "aip_offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "aip_projects",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    program_id = table.Column<int>(type: "int", nullable: false),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_projects", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_projects_aip_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "aip_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "aip_activities",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    project_id = table.Column<int>(type: "int", nullable: false),
                    ref_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    esre_code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    implementing_office = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    start_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    end_date = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    expected_outputs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    funding_source_id = table.Column<int>(type: "int", nullable: true),
                    funding_source_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ps = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    mooe = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    co = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    total = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    cc_adaptation = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    cc_mitigation = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    cc_typology_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aip_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_aip_activities_aip_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "aip_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_aip_activities_funding_sources_funding_source_id",
                        column: x => x.funding_source_id,
                        principalTable: "funding_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wfp_activities",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wfp_id = table.Column<int>(type: "int", nullable: false),
                    aip_activity_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_activities", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_activities_aip_activities_aip_activity_id",
                        column: x => x.aip_activity_id,
                        principalTable: "aip_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_activities_wfp_records_wfp_id",
                        column: x => x.wfp_id,
                        principalTable: "wfp_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wfp_expenditure_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wfp_activity_id = table.Column<int>(type: "int", nullable: false),
                    expenditure_type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    resources_needed = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    responsible_unit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    success_indicator = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    means_of_verification = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    account_id = table.Column<int>(type: "int", nullable: true),
                    account_number_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    account_title_snapshot = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    total_appropriation = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    apply_reserve = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    reserve_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    net_appropriation = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    q1 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    q2 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    q3 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    q4 = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    quarterly_total = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    funding_source_id = table.Column<int>(type: "int", nullable: true),
                    funding_source_snapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wfp_expenditure_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_wfp_expenditure_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_expenditure_lines_funding_sources_funding_source_id",
                        column: x => x.funding_source_id,
                        principalTable: "funding_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wfp_expenditure_lines_wfp_activities_wfp_activity_id",
                        column: x => x.wfp_activity_id,
                        principalTable: "wfp_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_number",
                table: "accounts",
                column: "account_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounts_title",
                table: "accounts",
                column: "account_title");

            migrationBuilder.CreateIndex(
                name: "IX_aip_activities_funding_source_id",
                table: "aip_activities",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_activities_project_id",
                table: "aip_activities",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_activities_ref_code",
                table: "aip_activities",
                column: "ref_code");

            migrationBuilder.CreateIndex(
                name: "UX_aip_activities_project_id_ref_code",
                table: "aip_activities",
                columns: new[] { "project_id", "ref_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_aip_record_id",
                table: "aip_offices",
                column: "aip_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_ref_code",
                table: "aip_offices",
                column: "ref_code");

            migrationBuilder.CreateIndex(
                name: "UX_aip_offices_aip_record_id_ref_code",
                table: "aip_offices",
                columns: new[] { "aip_record_id", "ref_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_programs_office_id",
                table: "aip_programs",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_programs_ref_code",
                table: "aip_programs",
                column: "ref_code");

            migrationBuilder.CreateIndex(
                name: "UX_aip_programs_office_id_ref_code",
                table: "aip_programs",
                columns: new[] { "office_id", "ref_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_projects_program_id",
                table: "aip_projects",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_projects_ref_code",
                table: "aip_projects",
                column: "ref_code");

            migrationBuilder.CreateIndex(
                name: "UX_aip_projects_program_id_ref_code",
                table: "aip_projects",
                columns: new[] { "program_id", "ref_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_aip_records_ldip_id",
                table: "aip_records",
                column: "ldip_id");

            migrationBuilder.CreateIndex(
                name: "IX_aip_records_uploaded_by",
                table: "aip_records",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "IX_aip_source_id",
                table: "aip_records",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_changed_at",
                table: "audit_log",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_changed_by",
                table: "audit_log",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_table_record",
                table: "audit_log",
                columns: new[] { "table_name", "record_id" });

            migrationBuilder.CreateIndex(
                name: "IX_funding_sources_code",
                table: "funding_sources",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ldip_records_created_by",
                table: "ldip_records",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_ldip_records_ref_code",
                table: "ldip_records",
                column: "ref_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ldip_source_id",
                table: "ldip_records",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "IX_offices_office_code",
                table: "offices",
                column: "office_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wfp_activities_aip_act_id",
                table: "wfp_activities",
                column: "aip_activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_activities_wfp_id",
                table: "wfp_activities",
                column: "wfp_id");

            migrationBuilder.CreateIndex(
                name: "UX_wfp_activities_wfp_id_aip_activity_id",
                table: "wfp_activities",
                columns: new[] { "wfp_id", "aip_activity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wfp_exp_wfp_activity_id",
                table: "wfp_expenditure_lines",
                column: "wfp_activity_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_expenditure_lines_account_id",
                table: "wfp_expenditure_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_expenditure_lines_funding_source_id",
                table: "wfp_expenditure_lines",
                column: "funding_source_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_records_aip_record_id",
                table: "wfp_records",
                column: "aip_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_records_created_by",
                table: "wfp_records",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_records_office_id",
                table: "wfp_records",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_wfp_source_id",
                table: "wfp_records",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "UX_wfp_records_aip_record_id_office_id",
                table: "wfp_records",
                columns: new[] { "aip_record_id", "office_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "wfp_expenditure_lines");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "wfp_activities");

            migrationBuilder.DropTable(
                name: "aip_activities");

            migrationBuilder.DropTable(
                name: "wfp_records");

            migrationBuilder.DropTable(
                name: "aip_projects");

            migrationBuilder.DropTable(
                name: "funding_sources");

            migrationBuilder.DropTable(
                name: "offices");

            migrationBuilder.DropTable(
                name: "aip_programs");

            migrationBuilder.DropTable(
                name: "aip_offices");

            migrationBuilder.DropTable(
                name: "aip_records");

            migrationBuilder.DropTable(
                name: "ldip_records");
        }
    }
}
