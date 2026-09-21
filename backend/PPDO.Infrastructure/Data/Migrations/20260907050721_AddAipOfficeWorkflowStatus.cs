using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAipOfficeWorkflowStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_aip_offices_office_id",
                table: "aip_offices");

            migrationBuilder.AddColumn<string>(
                name: "workflow_status",
                table: "aip_offices",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_workflow_status",
                table: "aip_offices",
                columns: new[] { "office_id", "workflow_status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_aip_offices_workflow_status",
                table: "aip_offices");

            migrationBuilder.DropColumn(
                name: "workflow_status",
                table: "aip_offices");

            migrationBuilder.CreateIndex(
                name: "IX_aip_offices_office_id",
                table: "aip_offices",
                column: "office_id");
        }
    }
}
