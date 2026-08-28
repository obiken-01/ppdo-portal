using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    // Fixed seed GUID — must never change after the migration is applied.
    private static readonly Guid SuperAdminId = new("20000000-0000-0000-0000-000000000001");

    // Fixed timestamp so re-running the seed is idempotent.
    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Pre-computed BCrypt hash (work factor 11) of the SuperAdmin bootstrap password (RAL-274).
    //
    // The PLAINTEXT IS DELIBERATELY NOT RECORDED HERE, in CLAUDE.md, or anywhere else in this
    // repository — this repository is public. It lives in the maintainer's password manager; ask
    // before standing up a new environment. Storing the hash is fine and is the point of a hash.
    //
    // The previous value was a hash of a password that WAS written down here and in CLAUDE.md,
    // which put it on the internet. Do not reintroduce that pattern: if this ever needs rotating,
    // generate the replacement out of band and commit only the hash.
    //
    // The seeded row also carries MustChangePassword = true, so a fresh environment is forced to
    // rotate at first login rather than relying on anyone reading a warning comment.
    private const string DefaultSuperAdminHash =
        "$2a$11$lOZMuB5SI/QZZe8xeWgYUuuHExMXKhav1hn.1eGPK9zHrCJRkHM/K";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(u => u.Email)
            .HasMaxLength(256);

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.Role)
            .IsRequired();

        builder.Property(u => u.Position)
            .HasMaxLength(100);

        // Legacy PascalCase table — new columns follow the table's existing convention.
        // Nullable: null means "no preference", which the resolver treats as fall-through.
        builder.Property(u => u.LandingPage);

        builder.Property(u => u.ContactNo)
            .HasMaxLength(50);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // OverrideCanAccess* and OverrideCanManage* columns are nullable bool — no extra config needed.

        // Refresh token — nullable; cleared on logout, rotated on every refresh.
        // 64 random bytes base64-encoded = 88 chars; nvarchar(100) gives a small margin.
        builder.Property(u => u.RefreshToken)
            .HasMaxLength(100);

        // RefreshTokenExpiry — nullable DateTime; no extra config needed.

        // ── Password reset (RAL-253) — legacy PascalCase table, no HasColumnName needed ────────
        // RecoveryQuestionKey — nullable enum, no extra config needed.

        builder.Property(u => u.MustChangePassword)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.OverrideCanManagePpdoAllocation)
            .HasColumnName("OverrideCanManageAllocation");

        // OverrideCanManagePboCeiling (RAL-243) and OverrideCanReviewBudgetPlanning (RAL-244)
        // — new nullable bit columns on the legacy PascalCase Users table, so the property
        // name IS the column name. No HasColumnName needed; unlike the line above, neither
        // was ever renamed.

        builder.Property(u => u.RecoveryAttemptCount)
            .IsRequired()
            .HasDefaultValue(0);

        // RecoveryAnswerHash, RecoveryFirstAttemptAt — nullable, no extra config needed.

        // Unique index on Username — the login identity.
        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("IX_Users_Username");

        // Filtered unique index on Email — multiple NULL emails are allowed
        // (SQL Server treats multiple NULLs as duplicates in a plain unique index).
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL")
            .HasDatabaseName("IX_Users_Email");

        // FK: Users.DivisionId → divisions.id (v1.2 — RAL-97). Carries the user's scope + flags.
        // Restrict: a division with users assigned cannot be hard-deleted (soft-delete via IsActive).
        builder.HasOne(u => u.Division)
            .WithMany(d => d.Users)
            .HasForeignKey(u => u.DivisionId)
            .HasConstraintName("FK_Users_divisions_DivisionId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.DivisionId)
            .HasDatabaseName("IX_Users_DivisionId");

        // FK: Users.OfficeId → offices.id (v1.1 — non-PPDO office users).
        // Restrict: an office that has users assigned cannot be hard-deleted
        // (offices are soft-deleted via IsActive anyway).
        builder.HasOne(u => u.Office)
            .WithMany(o => o.Users)
            .HasForeignKey(u => u.OfficeId)
            .HasConstraintName("FK_Users_offices_OfficeId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => u.OfficeId)
            .HasDatabaseName("IX_Users_OfficeId");

        // ── Seed data ─────────────────────────────────────────────────────────
        // One SuperAdmin user created at first deploy.
        // DivisionId is null — SuperAdmin bypasses all permission flag checks.
        // Bootstrap password: see DefaultSuperAdminHash above — the plaintext is intentionally
        // absent from this repository. MustChangePassword = true forces a rotation at first login.
        builder.HasData(new User
        {
            Id           = SuperAdminId,
            FullName     = "System Administrator",
            Username     = "superadmin",
            Email        = "superadmin@ppdo.gov.ph",
            PasswordHash = DefaultSuperAdminHash,
            Role         = UserRole.SuperAdmin,
            MustChangePassword = true,
            DivisionId   = null,
            Position     = "System Administrator",
            IsActive     = true,
            CreatedAt    = SeedDate,
            UpdatedAt    = SeedDate,
        });
    }
}
