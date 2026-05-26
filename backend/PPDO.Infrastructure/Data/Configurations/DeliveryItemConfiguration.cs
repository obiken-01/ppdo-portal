using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class DeliveryItemConfiguration : IEntityTypeConfiguration<DeliveryItem>
{
    public void Configure(EntityTypeBuilder<DeliveryItem> builder)
    {
        builder.ToTable("DeliveryItems");

        builder.HasKey(di => di.Id);

        // 4 decimal places — QtyDelivered may be fractional for some supply types.
        builder.Property(di => di.QtyDelivered)
            .HasColumnType("decimal(18,4)");

        // FK: DeliveryItems.DeliveryId → Deliveries.Id
        // Cascade: deleting a delivery removes all its item lines.
        builder.HasOne(di => di.Delivery)
            .WithMany(d => d.Items)
            .HasForeignKey(di => di.DeliveryId)
            .HasConstraintName("FK_DeliveryItems_Deliveries_DeliveryId")
            .OnDelete(DeleteBehavior.Cascade);

        // FK: DeliveryItems.PRItemId → PRItems.Id
        // Restrict: a PRItem with delivery records must not be deleted independently.
        builder.HasOne(di => di.PRItem)
            .WithMany()
            .HasForeignKey(di => di.PRItemId)
            .HasConstraintName("FK_DeliveryItems_PRItems_PRItemId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(di => di.DeliveryId)
            .HasDatabaseName("IX_DeliveryItems_DeliveryId");

        builder.HasIndex(di => di.PRItemId)
            .HasDatabaseName("IX_DeliveryItems_PRItemId");
    }
}
