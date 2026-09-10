using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class OfficeConfiguration : IEntityTypeConfiguration<Office>
{
    public void Configure(EntityTypeBuilder<Office> builder)
    {
        // v1.1 budget planning tables use snake_case names per docs/v1.1/DB_Model.md.
        builder.ToTable("offices");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.OfficeCode)
            .HasColumnName("office_code")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(o => o.OfficeName)
            .HasColumnName("office_name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.OfficeRefCode)
            .HasColumnName("office_ref_code")
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(o => o.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(o => o.IsHostOffice)
            .HasColumnName("is_host_office")
            .IsRequired()
            .HasDefaultValue(false);

        // Null means "no default" — the resolver falls through to the next level.
        builder.Property(o => o.LandingPage)
            .HasColumnName("landing_page");

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(o => o.OfficeCode)
            .IsUnique()
            .HasDatabaseName("IX_offices_office_code");

        // At most one host office, enforced by the database rather than by a service check that
        // a second code path could forget to call (DECISION F, RAL-258). Filtered so the many
        // false rows do not collide with each other.
        builder.HasIndex(o => o.IsHostOffice)
            .IsUnique()
            .HasFilter("[is_host_office] = 1")
            .HasDatabaseName("UX_offices_is_host_office");
    }
}
