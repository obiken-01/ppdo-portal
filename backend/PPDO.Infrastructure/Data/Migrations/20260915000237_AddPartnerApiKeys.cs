using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnerApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverrideCanManageApiKeys",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "partner_api_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    partner_name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    key_prefix = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    key_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    all_offices = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_used_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "FK_partner_api_keys_Users_created_by",
                        column: x => x.created_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_partner_api_keys_Users_revoked_by",
                        column: x => x.revoked_by_id,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partner_api_key_offices",
                columns: table => new
                {
                    key_id = table.Column<int>(type: "int", nullable: false),
                    office_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_api_key_offices", x => new { x.key_id, x.office_id });
                    table.ForeignKey(
                        name: "FK_partner_api_key_offices_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_partner_api_key_offices_partner_api_keys_key_id",
                        column: x => x.key_id,
                        principalTable: "partner_api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "partner_api_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    key_id = table.Column<int>(type: "int", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    office_code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    fiscal_year = table.Column<int>(type: "int", nullable: true),
                    status_code = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_api_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_partner_api_requests_partner_api_keys_key_id",
                        column: x => x.key_id,
                        principalTable: "partner_api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "OverrideCanManageApiKeys",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_partner_api_key_offices_office_id",
                table: "partner_api_key_offices",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "IX_partner_api_keys_created_by_id",
                table: "partner_api_keys",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "IX_partner_api_keys_revoked_by_id",
                table: "partner_api_keys",
                column: "revoked_by_id");

            migrationBuilder.CreateIndex(
                name: "UX_partner_api_keys_key_prefix",
                table: "partner_api_keys",
                column: "key_prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_partner_api_requests_key_id_requested_at",
                table: "partner_api_requests",
                columns: new[] { "key_id", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_api_key_offices");

            migrationBuilder.DropTable(
                name: "partner_api_requests");

            migrationBuilder.DropTable(
                name: "partner_api_keys");

            migrationBuilder.DropColumn(
                name: "OverrideCanManageApiKeys",
                table: "Users");
        }
    }
}
