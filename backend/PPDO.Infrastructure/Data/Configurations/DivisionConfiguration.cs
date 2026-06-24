using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// EF configuration for the configurable <see cref="Division"/> table (v1.2 — RAL-97).
/// snake_case table/columns per docs/NAMING_CONVENTIONS.md. No seed rows — loaded via CSV.
/// Upsert key is (office_id, name): name is required, code is nullable.
/// </summary>
public sealed class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
    public void Configure(EntityTypeBuilder<Division> builder)
    {
        builder.ToTable("divisions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");

        builder.Property(d => d.OfficeId)
            .HasColumnName("office_id")
            .IsRequired();

        builder.Property(d => d.Code)
            .HasColumnName("code")
            .HasMaxLength(20)
            .IsRequired(false);

        builder.Property(d => d.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        // ── Feature flags ─────────────────────────────────────────────────────
        builder.Property(d => d.CanAccessInventory)
            .HasColumnName("can_access_inventory").IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CanAccessReports)
            .HasColumnName("can_access_reports").IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CanManageUsers)
            .HasColumnName("can_manage_users").IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CanManageResourceLinks)
            .HasColumnName("can_manage_resource_links").IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CanAccessBudgetPlanning)
            .HasColumnName("can_access_budget_planning").IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CanUploadAip)
            .HasColumnName("can_upload_aip").IsRequired().HasDefaultValue(false);
        builder.Property(d => d.CanManageConfig)
            .HasColumnName("can_manage_config").IsRequired().HasDefaultValue(false);

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        // FK: divisions.office_id → offices.id. Restrict — an office with divisions
        // cannot be hard-deleted (offices are soft-deleted via IsActive anyway).
        builder.HasOne(d => d.Office)
            .WithMany()
            .HasForeignKey(d => d.OfficeId)
            .HasConstraintName("FK_divisions_offices_office_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.OfficeId)
            .HasDatabaseName("IX_divisions_office_id");

        // Upsert key — name unique within an office (code is nullable).
        builder.HasIndex(d => new { d.OfficeId, d.Name })
            .IsUnique()
            .HasDatabaseName("IX_divisions_office_id_name");
    }
}
