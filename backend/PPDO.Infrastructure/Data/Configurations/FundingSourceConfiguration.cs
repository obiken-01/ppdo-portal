using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class FundingSourceConfiguration : IEntityTypeConfiguration<FundingSource>
{
    public void Configure(EntityTypeBuilder<FundingSource> builder)
    {
        builder.ToTable("funding_sources");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");

        builder.Property(f => f.Code)
            .HasColumnName("code")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(f => f.Description)
            .HasColumnName("description");  // nvarchar(max), nullable

        builder.Property(f => f.Color)
            .HasColumnName("color")
            .HasMaxLength(7);

        builder.Property(f => f.Aliases)
            .HasColumnName("aliases");  // nvarchar(max), nullable — pipe-delimited alternate names

        builder.Property(f => f.OfficeId)
            .HasColumnName("office_id")
            .IsRequired(false);   // null = province-wide, PPDO-owned (PPDO-109, D5)

        builder.Property(f => f.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        // FK: funding_sources.office_id → offices.id (PPDO-109). NoAction, not Restrict: offices are
        // soft-deleted via IsActive and this column is nullable, so there is no cascade path worth
        // configuring — and NoAction keeps SQL Server from inventing one through the other office FKs.
        builder.HasOne(f => f.Office)
            .WithMany()
            .HasForeignKey(f => f.OfficeId)
            .HasConstraintName("FK_funding_sources_offices_office_id")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(f => f.OfficeId)
            .HasDatabaseName("IX_funding_sources_office_id");

        // ⚠️ Code stays unique across EVERY row, shared and office-owned alike (PPDO-109, D6).
        // Deliberately NOT scoped to (office_id, code): province-wide totals group by code, so two
        // offices must not be able to give one code two meanings, and an office must not be able to
        // shadow "GF" with a fund of its own.
        builder.HasIndex(f => f.Code)
            .IsUnique()
            .HasDatabaseName("IX_funding_sources_code");
    }
}
