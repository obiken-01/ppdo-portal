using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountExpenseClassAndDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "default_apply_reserve",
                table: "accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "default_nature",
                table: "accounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // 1. Add expense_class as temporarily nullable so the backfill can run before
            //    the NOT NULL constraint is applied (RAL-117 — was previously derived from
            //    the account_number prefix at read time; now a stored, editable column).
            migrationBuilder.AddColumn<string>(
                name: "expense_class",
                table: "accounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // 2. Backfill from the same prefix rule the app used to derive AccountType with:
            //    5-01- = PS, 5-02- = MOOE, 5-03- = CO, anything else = Other.
            migrationBuilder.Sql(@"
UPDATE accounts
SET expense_class = CASE
    WHEN account_number LIKE '5-01-%' THEN 'PS'
    WHEN account_number LIKE '5-02-%' THEN 'MOOE'
    WHEN account_number LIKE '5-03-%' THEN 'CO'
    ELSE 'Other'
END;
");

            // 3. Now all rows have a value — enforce NOT NULL.
            migrationBuilder.AlterColumn<string>(
                name: "expense_class",
                table: "accounts",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_apply_reserve",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "default_nature",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "expense_class",
                table: "accounts");
        }
    }
}
