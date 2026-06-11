using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class WfpExpenditureLineConfiguration : IEntityTypeConfiguration<WfpExpenditureLine>
{
    public void Configure(EntityTypeBuilder<WfpExpenditureLine> builder)
    {
        builder.ToTable("wfp_expenditure_lines");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.WfpActivityId)
            .HasColumnName("wfp_activity_id")
            .IsRequired();

        builder.Property(e => e.ExpenditureType)
            .HasColumnName("expenditure_type")
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(e => e.ResourcesNeeded)
            .HasColumnName("resources_needed");  // nvarchar(max), nullable

        builder.Property(e => e.ResponsibleUnit)
            .HasColumnName("responsible_unit")
            .HasMaxLength(200);

        builder.Property(e => e.SuccessIndicator)
            .HasColumnName("success_indicator");  // nvarchar(max), nullable

        builder.Property(e => e.MeansOfVerification)
            .HasColumnName("means_of_verification");  // nvarchar(max), nullable

        builder.Property(e => e.AccountId)
            .HasColumnName("account_id");

        builder.Property(e => e.AccountNumberSnapshot)
            .HasColumnName("account_number_snapshot")
            .HasMaxLength(20);

        builder.Property(e => e.AccountTitleSnapshot)
            .HasColumnName("account_title_snapshot")
            .HasMaxLength(300);

        builder.Property(e => e.TotalAppropriation)
            .HasColumnName("total_appropriation")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.ApplyReserve)
            .HasColumnName("apply_reserve")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ReserveAmount)
            .HasColumnName("reserve_amount")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.NetAppropriation)
            .HasColumnName("net_appropriation")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.Q1)
            .HasColumnName("q1")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.Q2)
            .HasColumnName("q2")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.Q3)
            .HasColumnName("q3")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.Q4)
            .HasColumnName("q4")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.QuarterlyTotal)
            .HasColumnName("quarterly_total")
            .HasColumnType("decimal(18,2)");

        builder.Property(e => e.FundingSourceId)
            .HasColumnName("funding_source_id");

        builder.Property(e => e.FundingSourceSnapshot)
            .HasColumnName("funding_source_snapshot")
            .HasMaxLength(20);

        builder.Property(e => e.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired()
            .HasDefaultValue(0);

        builder.HasIndex(e => e.WfpActivityId)
            .HasDatabaseName("IX_wfp_exp_wfp_activity_id");

        // Cascade: lines belong to their WFP activity.
        builder.HasOne(e => e.WfpActivity)
            .WithMany(a => a.ExpenditureLines)
            .HasForeignKey(e => e.WfpActivityId)
            .HasConstraintName("FK_wfp_expenditure_lines_wfp_activities_wfp_activity_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .HasConstraintName("FK_wfp_expenditure_lines_accounts_account_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FundingSource)
            .WithMany()
            .HasForeignKey(e => e.FundingSourceId)
            .HasConstraintName("FK_wfp_expenditure_lines_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
