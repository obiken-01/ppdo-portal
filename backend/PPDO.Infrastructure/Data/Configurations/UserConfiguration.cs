using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
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
    }
}
