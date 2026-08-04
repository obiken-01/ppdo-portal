using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("stock_balances");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.StockNo)
            .HasColumnName("stock_no")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(b => b.CountedQty)
            .HasColumnName("counted_qty")
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(b => b.SystemOnHandAtEntry)
            .HasColumnName("system_on_hand_at_entry")
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(b => b.VarianceQty)
            .HasColumnName("variance_qty")
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(b => b.EffectiveDate)
            .HasColumnName("effective_date")
            .IsRequired();

        builder.Property(b => b.Reason)
            .HasColumnName("reason")
            .HasMaxLength(500);

        builder.Property(b => b.RecordedByUserId)
            .HasColumnName("recorded_by_user_id")
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(b => b.StockNo)
            .HasDatabaseName("IX_stock_balances_stock_no");

        // Backs both the bulk-import upsert lookup and duplicate-guard queries.
        builder.HasIndex(b => new { b.StockNo, b.EffectiveDate })
            .IsUnique()
            .HasDatabaseName("IX_stock_balances_stock_no_effective_date");
    }
}
