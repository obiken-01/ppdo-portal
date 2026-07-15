using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class DivisionAllocationConfiguration : IEntityTypeConfiguration<DivisionAllocation>
{
    public void Configure(EntityTypeBuilder<DivisionAllocation> builder)
    {
        builder.ToTable("division_allocations");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.DivisionId)
            .HasColumnName("division_id")
            .IsRequired();

        builder.Property(a => a.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(a => a.FundingSourceId)
            .HasColumnName("funding_source_id")
            .IsRequired();

        builder.Property(a => a.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(a => new { a.DivisionId, a.FiscalYear, a.FundingSourceId })
            .IsUnique()
            .HasDatabaseName("IX_division_allocations_div_fy_fund");

        builder.HasOne(a => a.Division)
            .WithMany()
            .HasForeignKey(a => a.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.FundingSource)
            .WithMany()
            .HasForeignKey(a => a.FundingSourceId)
            .HasConstraintName("FK_division_allocations_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
