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

    // Pre-computed BCrypt hash (work factor 11) of the default password: PPDOAdmin2026!
    // ⚠️  MUST be changed on first deploy — use PUT /api/auth/change-password after login.
    private const string DefaultSuperAdminHash =
        "$2a$11$HaBMPo0zwTrOTJt3jqY8Ou8RNcYTfedkTJCDuP2AW5RFvofq0wQEO";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.PasswordHash)
            .IsRequired();

        builder.Property(u => u.Role)
            .IsRequired();

        builder.Property(u => u.Division)
            .IsRequired();

        builder.Property(u => u.Position)
            .HasMaxLength(100);

        builder.Property(u => u.ContactNo)
            .HasMaxLength(50);

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // OverrideCanAccess* columns are nullable bool — no extra config needed.

        // Unique index on Email — used as login username.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("IX_Users_Email");

        // FK: Users.GroupId → PermissionGroups.Id
        // SetNull: deactivating a permission group clears the user's GroupId rather than deleting the user.
        builder.HasOne(u => u.Group)
            .WithMany(g => g.Users)
            .HasForeignKey(u => u.GroupId)
            .HasConstraintName("FK_Users_PermissionGroups_GroupId")
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(u => u.GroupId)
            .HasDatabaseName("IX_Users_GroupId");

        // ── Seed data ─────────────────────────────────────────────────────────
        // One SuperAdmin user created at first deploy.
        // GroupId is null — SuperAdmin bypasses all permission group flag checks.
        // Default password: PPDOAdmin2026!  (BCrypt work factor 11, pre-computed)
        // ⚠️  Change this password immediately after the first login.
        builder.HasData(new User
        {
            Id           = SuperAdminId,
            FullName     = "System Administrator",
            Email        = "superadmin@ppdo.gov.ph",
            PasswordHash = DefaultSuperAdminHash,
            Role         = UserRole.SuperAdmin,
            Division     = Division.Admin,
            GroupId      = null,
            Position     = "System Administrator",
            IsActive     = true,
            CreatedAt    = SeedDate,
            UpdatedAt    = SeedDate,
        });
    }
}
