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

        builder.HasIndex(f => f.Code)
            .IsUnique()
            .HasDatabaseName("IX_funding_sources_code");
    }
}
