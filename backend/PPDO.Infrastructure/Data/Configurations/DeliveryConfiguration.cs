using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("Deliveries");

        builder.HasKey(d => d.Id);

        // DeliveryRef format: DEL-YYYYMMDD-XXXXX (e.g. DEL-20260526-A3F7B, max 20 chars)
        builder.Property(d => d.DeliveryRef)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(d => d.ReceivedBy)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Supplier)
            .HasMaxLength(200);

        builder.Property(d => d.Remarks);  // nvarchar(max), nullable

        // Unique index on DeliveryRef — used in PR Report Section 3 lookups.
        builder.HasIndex(d => d.DeliveryRef)
            .IsUnique()
            .HasDatabaseName("IX_Deliveries_DeliveryRef");

        // FK: Deliveries.PRId → PurchaseRequests.Id
        // Restrict: do not allow deleting a PR that has delivery records.
        builder.HasOne(d => d.PurchaseRequest)
            .WithMany(pr => pr.Deliveries)
            .HasForeignKey(d => d.PRId)
            .HasConstraintName("FK_Deliveries_PurchaseRequests_PRId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(d => d.PRId)
            .HasDatabaseName("IX_Deliveries_PRId");
    }
}
