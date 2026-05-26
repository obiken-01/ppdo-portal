using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> builder)
    {
        builder.ToTable("PurchaseRequests");

        builder.HasKey(pr => pr.Id);

        // PRNo format: 101-1041-GF-YYYY-MM-DD-XXX (max ~30 chars)
        builder.Property(pr => pr.PRNo)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(pr => pr.Department)
            .IsRequired()
            .HasMaxLength(100)
            .HasDefaultValue("PPDO");

        builder.Property(pr => pr.Division)
            .IsRequired();

        builder.Property(pr => pr.Fund)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pr => pr.RequestedBy)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pr => pr.Position)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(pr => pr.ApprovedBy)
            .HasMaxLength(100);

        builder.Property(pr => pr.ApprovingPosition)
            .HasMaxLength(100);

        builder.Property(pr => pr.AIPCode)
            .HasMaxLength(50);

        builder.Property(pr => pr.AccountNo)
            .HasMaxLength(50);

        builder.Property(pr => pr.AccountTitle)
            .HasMaxLength(200);

        // Program / Project / Activity — long text (textarea in UI, up to 120 chars)
        builder.Property(pr => pr.Program)
            .HasMaxLength(120);

        builder.Property(pr => pr.Project)
            .HasMaxLength(120);

        builder.Property(pr => pr.Activity)
            .HasMaxLength(120);

        builder.Property(pr => pr.SAINo)
            .HasMaxLength(50);

        builder.Property(pr => pr.ALOBSNo)
            .HasMaxLength(50);

        builder.Property(pr => pr.TotalAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(pr => pr.Status)
            .IsRequired()
            .HasDefaultValue(PRStatus.Open);

        // Unique index on PRNo — referenced in delivery and report lookups.
        builder.HasIndex(pr => pr.PRNo)
            .IsUnique()
            .HasDatabaseName("IX_PurchaseRequests_PRNo");

        // Query indexes — division scope + status filtering are the two most common filters.
        builder.HasIndex(pr => pr.Division)
            .HasDatabaseName("IX_PurchaseRequests_Division");

        builder.HasIndex(pr => pr.Status)
            .HasDatabaseName("IX_PurchaseRequests_Status");

        builder.HasIndex(pr => pr.CreatedById)
            .HasDatabaseName("IX_PurchaseRequests_CreatedById");

        // FK: PurchaseRequests.CreatedById → Users.Id
        // Restrict: do not delete a user who has submitted PRs — deactivate instead.
        builder.HasOne(pr => pr.CreatedBy)
            .WithMany()
            .HasForeignKey(pr => pr.CreatedById)
            .HasConstraintName("FK_PurchaseRequests_Users_CreatedById")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
