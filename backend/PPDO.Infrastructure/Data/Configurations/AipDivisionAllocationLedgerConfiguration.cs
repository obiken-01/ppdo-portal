using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// V18-45 / PPDO-55. Mirrors <see cref="WfpDivisionAllocationLedgerConfiguration"/> in shape, with
/// the one deliberate difference: the fourth key column is <c>aip_activity_id</c>, not a record id.
/// Reservations are keyed per activity so the deferred relief rule can be implemented later without
/// a second migration — see <see cref="AipDivisionAllocationLedger"/>.
/// </summary>
public sealed class AipDivisionAllocationLedgerConfiguration : IEntityTypeConfiguration<AipDivisionAllocationLedger>
{
    public void Configure(EntityTypeBuilder<AipDivisionAllocationLedger> builder)
    {
        builder.ToTable("aip_division_allocation_ledger");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.DivisionId)
            .HasColumnName("division_id")
            .IsRequired();

        builder.Property(e => e.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(e => e.FundingSourceId)
            .HasColumnName("funding_source_id")
            .IsRequired();

        builder.Property(e => e.AipActivityId)
            .HasColumnName("aip_activity_id")
            .IsRequired();

        builder.Property(e => e.AllocatedAmountSnapshot)
            .HasColumnName("allocated_amount_snapshot")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.ReservedAmount)
            .HasColumnName("reserved_amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // The upsert key. Unique so a double-post cannot silently double-count a reservation.
        builder.HasIndex(e => new { e.DivisionId, e.FiscalYear, e.FundingSourceId, e.AipActivityId })
            .IsUnique()
            .HasDatabaseName("IX_aip_division_allocation_ledger_division_fy_fund_activity");

        // Restrict: divisions are soft-deleted config rows, never hard-deleted while referenced.
        builder.HasOne(e => e.Division)
            .WithMany()
            .HasForeignKey(e => e.DivisionId)
            .HasConstraintName("FK_aip_division_allocation_ledger_divisions_division_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(e => e.FundingSource)
            .WithMany()
            .HasForeignKey(e => e.FundingSourceId)
            .HasConstraintName("FK_aip_division_allocation_ledger_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);

        // ⚠️ NoAction, NOT Cascade — and this is where it diverges from the WFP ledger.
        //
        // The WFP ledger cascades from wfp_records: a record is one root, so one cascade path.
        // Here the parent is an AIP *activity*, which already sits under a cascade chain
        // (record → office → program → project → activity). Adding a second cascade path into
        // this table alongside the divisions and funding_sources FKs is exactly what SQL Server
        // rejects with "may cause cycles or multiple cascade paths" at migration time.
        //
        // Rows are therefore deleted explicitly when an activity is deleted, in the same
        // transaction — see the delete path in the entry service. A reservation whose activity is
        // gone is meaningless, so leaving one behind would overstate consumed allocation and
        // block a submit for work that no longer exists.
        builder.HasOne(e => e.AipActivity)
            .WithMany()
            .HasForeignKey(e => e.AipActivityId)
            .HasConstraintName("FK_aip_division_allocation_ledger_aip_activities_aip_activity_id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
