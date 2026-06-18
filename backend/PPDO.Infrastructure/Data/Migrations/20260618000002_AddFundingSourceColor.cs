using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundingSourceColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "funding_sources",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                table: "funding_sources");
        }
    }
}
