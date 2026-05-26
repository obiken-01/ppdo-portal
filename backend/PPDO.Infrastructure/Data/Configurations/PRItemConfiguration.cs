using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class PRItemConfiguration : IEntityTypeConfiguration<PRItem>
{
    public void Configure(EntityTypeBuilder<PRItem> builder)
    {
        builder.ToTable("PRItems");

        builder.HasKey(i => i.Id);

        // StockNo nullable — item may not exist in ItemMaster yet (new/unknown stock).
        builder.Property(i => i.StockNo)
            .HasMaxLength(50);

        builder.Property(i => i.Description)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(i => i.Unit)
            .IsRequired()
            .HasMaxLength(50);

        // 4 decimal places for supply quantities (e.g. 2.5 reams)
        builder.Property(i => i.Quantity)
            .HasColumnType("decimal(18,4)");

        builder.Property(i => i.UnitCost)
            .HasColumnType("decimal(18,2)");

        // TotalCost = Quantity × UnitCost — computed by PurchaseRequestService, stored here.
        builder.Property(i => i.TotalCost)
            .HasColumnType("decimal(18,2)");

        builder.Property(i => i.ItemType)
            .HasMaxLength(100);

        // FK: PRItems.PRId → PurchaseRequests.Id
        // Cascade: deleting a PR removes all its line items.
        builder.HasOne(i => i.PurchaseRequest)
            .WithMany(pr => pr.Items)
            .HasForeignKey(i => i.PRId)
            .HasConstraintName("FK_PRItems_PurchaseRequests_PRId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => i.PRId)
            .HasDatabaseName("IX_PRItems_PRId");
    }
}
