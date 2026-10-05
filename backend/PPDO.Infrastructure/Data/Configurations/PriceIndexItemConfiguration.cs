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

        // PGOM lists the same item under several stock card numbers; each is its own price index
        // row, so the identity is (name, unit, stock card no). SQL Server's unique index treats
        // NULL as a value, so two rows with the same name and unit and no stock card still collide.
        // ⚠️ HasFilter(null) is load-bearing: for a unique index over a nullable column the EF
        // SQL Server provider otherwise adds "WHERE stock_card_no IS NOT NULL", which would exempt
        // every row without a stock card from the uniqueness check altogether.
        builder.HasIndex(p => new { p.Name, p.Unit, p.StockCardNo })
            .IsUnique()
            .HasFilter(null)
            .HasDatabaseName("IX_price_index_items_name_unit_stock_card_no");
    }
}
