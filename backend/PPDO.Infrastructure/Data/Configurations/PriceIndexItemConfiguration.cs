using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class PriceIndexItemConfiguration : IEntityTypeConfiguration<PriceIndexItem>
{
    public void Configure(EntityTypeBuilder<PriceIndexItem> builder)
    {
        builder.ToTable("price_index_items");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(p => p.Unit)
            .HasColumnName("unit")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.UnitPrice)
            .HasColumnName("unit_price")
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Category)
            .HasColumnName("category")
            .HasMaxLength(100);

        builder.Property(p => p.StockCardNo)
            .HasColumnName("stock_card_no")
            .HasMaxLength(50);

        builder.Property(p => p.PriceUpdatedAt)
            .HasColumnName("price_updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.DaysEnabled)
            .HasColumnName("days_enabled")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(p => new { p.Name, p.Unit })
            .IsUnique()
            .HasDatabaseName("IX_price_index_items_name_unit");
    }
}
