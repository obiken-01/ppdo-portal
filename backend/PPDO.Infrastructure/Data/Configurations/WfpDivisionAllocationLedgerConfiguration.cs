using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class WfpDivisionAllocationLedgerConfiguration : IEntityTypeConfiguration<WfpDivisionAllocationLedger>
{
    public void Configure(EntityTypeBuilder<WfpDivisionAllocationLedger> builder)
    {
        builder.ToTable("wfp_division_allocation_ledger");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.DivisionId)
            .HasColumnName("division_id")
            .IsRequired();

        builder.Property(e => e.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(e => e.WfpRecordId)
            .HasColumnName("wfp_record_id")
            .IsRequired();

        builder.Property(e => e.AllocatedAmountSnapshot)
            .HasColumnName("allocated_amount_snapshot")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.UsedAmount)
            .HasColumnName("used_amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => new { e.DivisionId, e.FiscalYear, e.WfpRecordId })
            .IsUnique()
            .HasDatabaseName("IX_wfp_division_allocation_ledger_division_fy_wfp_record");

        // Restrict: divisions are soft-deleted config rows, never hard-deleted while referenced.
        builder.HasOne(e => e.Division)
            .WithMany()
            .HasForeignKey(e => e.DivisionId)
            .HasConstraintName("FK_wfp_division_allocation_ledger_divisions_division_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade: a ledger row's usage is meaningless once its WFP record is gone.
        builder.HasOne(e => e.WfpRecord)
            .WithMany()
            .HasForeignKey(e => e.WfpRecordId)
            .HasConstraintName("FK_wfp_division_allocation_ledger_wfp_records_wfp_record_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
