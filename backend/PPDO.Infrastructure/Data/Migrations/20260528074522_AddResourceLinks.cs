using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OverrideCanManageResourceLinks",
                table: "Users",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageResourceLinks",
                table: "PermissionGroups",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ResourceLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryOrder = table.Column<int>(type: "int", nullable: false),
                    LinkOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsAdminCreated = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceLinks_Users_SubmittedById",
                        column: x => x.SubmittedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Set CanManageResourceLinks = true for Admin Division Staff only.
            // Groups 002–006 remain at the column default (false) — no UPDATE needed.
            migrationBuilder.UpdateData(
                table: "PermissionGroups",
                keyColumn: "Id",
                keyValue: new Guid("10000000-0000-0000-0000-000000000001"),
                column: "CanManageResourceLinks",
                value: true);

            migrationBuilder.InsertData(
                table: "ResourceLinks",
                columns: new[] { "Id", "Category", "CategoryOrder", "CreatedAt", "IsActive", "IsAdminCreated", "LinkOrder", "SubmittedById", "Title", "UpdatedAt", "Url" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), "Supply & Property Management", 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 1, null, "Inventory of Supplies/Property & Equipment", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000002"), "Supply & Property Management", 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 2, null, "PPDO Transactions Tracker", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000003"), "Supply & Property Management", 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 3, null, "PPMP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000004"), "Supply & Property Management", 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 4, null, "PR Monitoring", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000005"), "Records Management", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 1, null, "Administrative Division Files", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000006"), "Records Management", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 2, null, "Calendar of Activities", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000007"), "Records Management", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 3, null, "PDC Files", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000008"), "Records Management", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 4, null, "Planning Division Files", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000009"), "Records Management", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 5, null, "RMED Files", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000010"), "Records Management", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 6, null, "Incoming Communications", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000011"), "Human Resource Management", 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 1, null, "Personnel Profile", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000012"), "Human Resource Management", 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 2, null, "201 Files", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000013"), "Human Resource Management", 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 3, null, "IPCR/DPCR", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000014"), "Human Resource Management", 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 4, null, "Leave", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000015"), "Human Resource Management", 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 5, null, "Training/s Attended", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000016"), "Financial Management", 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 1, null, "WFP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000017"), "Financial Management", 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 2, null, "AIP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000018"), "Financial Management", 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 3, null, "SAIP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000019"), "Financial Management", 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 4, null, "GAD WFP", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000020"), "Financial Management", 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 5, null, "20% Development Funds Report", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000021"), "General", 5, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 1, null, "E-Directory", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000022"), "General", 5, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 2, null, "Organizational Chart", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" },
                    { new Guid("30000000-0000-0000-0000-000000000023"), "General", 5, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, true, 3, null, "Citizen's Charter", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "https://placeholder.example.com" }
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: new Guid("20000000-0000-0000-0000-000000000001"),
                column: "OverrideCanManageResourceLinks",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLinks_Category_LinkOrder",
                table: "ResourceLinks",
                columns: new[] { "Category", "LinkOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLinks_IsActive",
                table: "ResourceLinks",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceLinks_SubmittedById",
                table: "ResourceLinks",
                column: "SubmittedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceLinks");

            migrationBuilder.DropColumn(
                name: "OverrideCanManageResourceLinks",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CanManageResourceLinks",
                table: "PermissionGroups");
        }
    }
}
