using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class WfpExpenditureConfiguration : IEntityTypeConfiguration<WfpExpenditure>
{
    public void Configure(EntityTypeBuilder<WfpExpenditure> builder)
    {
        builder.ToTable("wfp_expenditures");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.WfpActivityId)
            .HasColumnName("wfp_activity_id")
            .IsRequired();

        builder.Property(e => e.AccountId)
            .HasColumnName("account_id");

        builder.Property(e => e.AccountNumberSnapshot)
            .HasColumnName("account_number_snapshot")
            .HasMaxLength(20);

        builder.Property(e => e.AccountTitleSnapshot)
            .HasColumnName("account_title_snapshot")
            .HasMaxLength(300);

        builder.Property(e => e.Nature)
            .HasColumnName("nature")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(e => e.Frequency)
            .HasColumnName("frequency")
            .IsRequired()
            .HasMaxLength(5);

        builder.Property(e => e.FundingSourceId)
            .HasColumnName("funding_source_id");

        builder.Property(e => e.FundingSourceSnapshot)
            .HasColumnName("funding_source_snapshot")
            .HasMaxLength(20);

        builder.Property(e => e.FundingSourceNameSnapshot)
            .HasColumnName("funding_source_name_snapshot")
            .HasMaxLength(100);

        builder.Property(e => e.ApplyReserve)
            .HasColumnName("apply_reserve")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.ReserveAmount)
            .HasColumnName("reserve_amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.AnnualQuarterChoice)
            .HasColumnName("annual_quarter_choice");

        builder.Property(e => e.Q1)
            .HasColumnName("q1")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.Q2)
            .HasColumnName("q2")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.Q3)
            .HasColumnName("q3")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.Q4)
            .HasColumnName("q4")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.NetAppropriation)
            .HasColumnName("net_appropriation")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.TotalAppropriation)
            .HasColumnName("total_appropriation")
            .HasColumnType("decimal(18,2)")
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.WfpActivityId)
            .HasDatabaseName("IX_wfp_expenditures_wfp_activity_id");

        // Cascade: expenditures belong to their WFP activity.
        builder.HasOne(e => e.WfpActivity)
            .WithMany()
            .HasForeignKey(e => e.WfpActivityId)
            .HasConstraintName("FK_wfp_expenditures_wfp_activities_wfp_activity_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(e => e.Account)
            .WithMany()
            .HasForeignKey(e => e.AccountId)
            .HasConstraintName("FK_wfp_expenditures_accounts_account_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.FundingSource)
            .WithMany()
            .HasForeignKey(e => e.FundingSourceId)
            .HasConstraintName("FK_wfp_expenditures_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
