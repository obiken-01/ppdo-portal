using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class WfpExpenditurePeriodConfiguration : IEntityTypeConfiguration<WfpExpenditurePeriod>
{
    public void Configure(EntityTypeBuilder<WfpExpenditurePeriod> builder)
    {
        builder.ToTable("wfp_expenditure_periods");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.ExpenditureId)
            .HasColumnName("expenditure_id")
            .IsRequired();

        builder.Property(e => e.PeriodNo)
            .HasColumnName("period_no")
            .IsRequired();

        builder.Property(e => e.Amount)
            .HasColumnName("amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(e => new { e.ExpenditureId, e.PeriodNo })
            .IsUnique()
            .HasDatabaseName("IX_wfp_expenditure_periods_expenditure_id_period_no");

        builder.HasOne(e => e.Expenditure)
            .WithMany(x => x.Periods)
            .HasForeignKey(e => e.ExpenditureId)
            .HasConstraintName("FK_wfp_expenditure_periods_wfp_expenditures_expenditure_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
