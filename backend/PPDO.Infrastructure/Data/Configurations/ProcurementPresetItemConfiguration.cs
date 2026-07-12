using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class ProcurementPresetItemConfiguration : IEntityTypeConfiguration<ProcurementPresetItem>
{
    public void Configure(EntityTypeBuilder<ProcurementPresetItem> builder)
    {
        builder.ToTable("procurement_preset_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.PresetId)
            .HasColumnName("preset_id")
            .IsRequired();

        builder.Property(i => i.PriceIndexItemId)
            .HasColumnName("price_index_item_id");

        builder.Property(i => i.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(i => i.Unit)
            .HasColumnName("unit")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(i => i.DefaultQty)
            .HasColumnName("default_qty")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(i => i.PresetId)
            .HasDatabaseName("IX_procurement_preset_items_preset_id");

        // Cascade: line items belong to their preset.
        builder.HasOne(i => i.Preset)
            .WithMany(p => p.Items)
            .HasForeignKey(i => i.PresetId)
            .HasConstraintName("FK_procurement_preset_items_procurement_presets_preset_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: the price index is soft-deleted, never hard-deleted while referenced.
        builder.HasOne(i => i.PriceIndexItem)
            .WithMany()
            .HasForeignKey(i => i.PriceIndexItemId)
            .HasConstraintName("FK_procurement_preset_items_price_index_items_price_index_item_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
