using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping for <see cref="AipProcurementItem"/> (V18-80 / PPDO-54). snake_case table and columns —
/// a new table, so the new-table rule applies (<c>docs/NAMING_CONVENTIONS.md</c>).
///
/// Shaped on <c>WfpProcurementItemConfiguration</c>. ↩️ <c>period_no</c> was left off at first and
/// added 2026-09-14 as input-only quarters — see <see cref="AipProcurementItem.PeriodNo"/>.
/// </summary>
public sealed class AipProcurementItemConfiguration : IEntityTypeConfiguration<AipProcurementItem>
{
    public void Configure(EntityTypeBuilder<AipProcurementItem> builder)
    {
        builder.ToTable("aip_procurement_items");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ExpenditureId)
            .HasColumnName("expenditure_id")
            .IsRequired();

        // Default 1 so the migration places every existing item in Q1 — no saved total moves.
        builder.Property(e => e.PeriodNo)
            .HasColumnName("period_no")
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.PriceIndexItemId)
            .HasColumnName("price_index_item_id");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(e => e.Unit)
            .HasColumnName("unit")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.UnitPrice)
            .HasColumnName("unit_price")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.Qty)
            .HasColumnName("qty")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.NumberOfDays)
            .HasColumnName("number_of_days")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(1m);

        builder.Property(e => e.LineTotal)
            .HasColumnName("line_total")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(e => e.ExpenditureId)
            .HasDatabaseName("IX_aip_procurement_items_expenditure_id");

        builder.HasOne(e => e.Expenditure)
            .WithMany(x => x.ProcurementItems)
            .HasForeignKey(e => e.ExpenditureId)
            .HasConstraintName("FK_aip_procurement_items_aip_expenditures_expenditure_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, matching the WFP table: price index rows are soft-deleted, never hard-deleted
        // while a saved plan still references them.
        builder.HasOne(e => e.PriceIndexItem)
            .WithMany()
            .HasForeignKey(e => e.PriceIndexItemId)
            .HasConstraintName("FK_aip_procurement_items_price_index_items_price_index_item_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
