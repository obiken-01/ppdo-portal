using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class DistributionConfiguration : IEntityTypeConfiguration<Distribution>
{
    public void Configure(EntityTypeBuilder<Distribution> builder)
    {
        builder.ToTable("Distributions");

        builder.HasKey(d => d.Id);

        // IssueRef format: ISS-YYYYMMDD-XXXXX-N (e.g. ISS-20260526-A3F7B-1, max 30 chars)
        builder.Property(d => d.IssueRef)
            .IsRequired()
            .HasMaxLength(30);

        // Division is non-nullable — distributions always target a specific division.
        builder.Property(d => d.Division)
            .IsRequired();

        // 4 decimal places — matches QtyDelivered precision.
        builder.Property(d => d.QtyIssued)
            .HasColumnType("decimal(18,4)");

        builder.Property(d => d.IssuedBy)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(d => d.Remarks);  // nvarchar(max), nullable

        // Unique index on IssueRef — used in PR Report Section 3.
        builder.HasIndex(d => d.IssueRef)
            .IsUnique()
            .HasDatabaseName("IX_Distributions_IssueRef");

        // FK: Distributions.DeliveryItemId → DeliveryItems.Id
        // Cascade: deleting a delivery item removes all its division distributions.
        builder.HasOne(d => d.DeliveryItem)
            .WithMany(di => di.Distributions)
            .HasForeignKey(d => d.DeliveryItemId)
            .HasConstraintName("FK_Distributions_DeliveryItems_DeliveryItemId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.DeliveryItemId)
            .HasDatabaseName("IX_Distributions_DeliveryItemId");
    }
}
