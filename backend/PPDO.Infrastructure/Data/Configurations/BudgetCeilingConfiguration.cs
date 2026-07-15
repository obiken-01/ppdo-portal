using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class BudgetCeilingConfiguration : IEntityTypeConfiguration<BudgetCeiling>
{
    public void Configure(EntityTypeBuilder<BudgetCeiling> builder)
    {
        builder.ToTable("budget_ceilings");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.OfficeId)
            .HasColumnName("office_id")
            .IsRequired();

        builder.Property(c => c.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(c => c.FundingSourceId)
            .HasColumnName("funding_source_id")
            .IsRequired();

        builder.Property(c => c.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(c => new { c.OfficeId, c.FiscalYear, c.FundingSourceId })
            .IsUnique()
            .HasDatabaseName("IX_budget_ceilings_office_fy_fund");

        builder.HasOne(c => c.Office)
            .WithMany()
            .HasForeignKey(c => c.OfficeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.FundingSource)
            .WithMany()
            .HasForeignKey(c => c.FundingSourceId)
            .HasConstraintName("FK_budget_ceilings_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
