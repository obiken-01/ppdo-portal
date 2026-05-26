using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class ItemMasterConfiguration : IEntityTypeConfiguration<ItemMaster>
{
    public void Configure(EntityTypeBuilder<ItemMaster> builder)
    {
        builder.ToTable("ItemMasters");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.StockNo)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.Description)
            .IsRequired()
            .HasMaxLength(500);

        // Category nullable: null/empty = "★ NEW - review" displayed in Items Master UI.
        builder.Property(i => i.Category)
            .HasMaxLength(100);

        builder.Property(i => i.Unit)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.UnitCost)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.ItemType)
            .HasMaxLength(100);

        builder.Property(i => i.Remarks);  // nvarchar(max), nullable

        builder.Property(i => i.IsNewItem)
            .IsRequired()
            .HasDefaultValue(false);

        // Unique index on StockNo — bidirectional lookup (StockNo ↔ Description) depends on this.
        builder.HasIndex(i => i.StockNo)
            .IsUnique()
            .HasDatabaseName("IX_ItemMasters_StockNo");

        // Index for filtering unreviewed items on the Items Master dashboard.
        builder.HasIndex(i => i.IsNewItem)
            .HasDatabaseName("IX_ItemMasters_IsNewItem");
    }
}
