using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundingSourceNameSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "funding_source_name_snapshot",
                table: "wfp_expenditure_lines",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "funding_source_name_snapshot",
                table: "wfp_expenditure_lines");
        }
    }
}
