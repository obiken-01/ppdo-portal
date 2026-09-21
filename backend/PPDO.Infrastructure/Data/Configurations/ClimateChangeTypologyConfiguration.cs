using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class ClimateChangeTypologyConfiguration : IEntityTypeConfiguration<ClimateChangeTypology>
{
    public void Configure(EntityTypeBuilder<ClimateChangeTypology> builder)
    {
        builder.ToTable("climate_change_typologies");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Code)
            .HasColumnName("code")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Category)
            .HasColumnName("category")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.Description)
            .HasColumnName("description");  // nvarchar(max), nullable

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(t => t.Code)
            .IsUnique()
            .HasDatabaseName("IX_climate_change_typologies_code");
    }
}
