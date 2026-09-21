using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PPDO.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Rotates the SuperAdmin seed credential off the password that was published in this
    /// repository, and forces a change at first login (RAL-274).
    ///
    /// ⚠️ HAND-EDITED, DELIBERATELY. The scaffolded version issued a bare UpdateData keyed only on
    /// the seed GUID, which would overwrite the password on EVERY environment — including live
    /// installations whose SuperAdmin was already rotated out of band (prod and local were rotated
    /// 2026-08-26). That would have silently replaced a known-good password with one whose
    /// plaintext is not in this repository: a lockout, not an upgrade.
    ///
    /// The guard below restricts the update to rows still sitting on the old published hash, so it
    /// repairs untouched environments and leaves rotated ones exactly as they are.
    ///
    /// CLAUDE.md forbids editing migrations that have ALREADY BEEN APPLIED. This one is edited
    /// before its first run, which is the only moment the guard can be added.
    /// </summary>
    public partial class RotateSuperAdminSeedCredential : Migration
    {
        // The retired hash. Present only so the guard can recognise an unrotated row — this value
        // opens nothing, and the password behind it is public in this repository's history.
        private const string RetiredHash =
            "$2a$11$HaBMPo0zwTrOTJt3jqY8Ou8RNcYTfedkTJCDuP2AW5RFvofq0wQEO";

        private const string BootstrapHash =
            "$2a$11$lOZMuB5SI/QZZe8xeWgYUuuHExMXKhav1hn.1eGPK9zHrCJRkHM/K";

        private const string SeedId = "20000000-0000-0000-0000-000000000001";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                UPDATE [Users]
                SET    [PasswordHash]       = '{BootstrapHash}',
                       [MustChangePassword] = 1
                WHERE  [Id]           = '{SeedId}'
                  AND  [PasswordHash] = '{RetiredHash}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally a no-op.
            //
            // The scaffolded Down restored the retired hash — that is, a rollback would have put a
            // credential published on the internet back onto the SuperAdmin account. A migration
            // that reopens a security hole when reversed is worse than one that cannot be reversed.
            //
            // Reversing the schema is not needed here: this migration changes no schema, only a
            // seeded row. To move the SuperAdmin password, use the application's own change-password
            // flow rather than a migration.
        }
    }
}
