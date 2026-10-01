using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

// snake_case mappings for the nine investment proposal child tables (PPDO-154, spec §5). The FK to
// investment_proposals and its cascade are configured once, on InvestmentProposalConfiguration.

public sealed class InvestmentProposalBeneficiaryConfiguration : IEntityTypeConfiguration<InvestmentProposalBeneficiary>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalBeneficiary> builder)
    {
        builder.ToTable("investment_proposal_beneficiaries", t => t.HasCheckConstraint(
            "CK_investment_proposal_beneficiaries_section",
            InvestmentProposalSql.InList("section", InvestmentProposalBeneficiarySection.All)));

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.ProposalId).HasColumnName("proposal_id");
        builder.Property(b => b.Section).HasColumnName("section").IsRequired().HasMaxLength(10);
        builder.Property(b => b.Label).HasColumnName("label").IsRequired().HasMaxLength(1000);
        builder.Property(b => b.Male).HasColumnName("male");
        builder.Property(b => b.Female).HasColumnName("female");
        builder.Property(b => b.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(b => b.ProposalId).HasDatabaseName("IX_investment_proposal_beneficiaries_proposal_id");
    }
}

public sealed class InvestmentProposalBenefitConfiguration : IEntityTypeConfiguration<InvestmentProposalBenefit>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalBenefit> builder)
    {
        builder.ToTable("investment_proposal_benefits");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.ProposalId).HasColumnName("proposal_id");
        builder.Property(b => b.Sector).HasColumnName("sector").IsRequired().HasMaxLength(30);
        builder.Property(b => b.Benefit).HasColumnName("benefit").HasMaxLength(4000);
        builder.Property(b => b.Cost).HasColumnName("cost").HasMaxLength(4000);

        // Also serves as the proposal_id FK index.
        builder.HasIndex(b => new { b.ProposalId, b.Sector })
            .IsUnique()
            .HasDatabaseName("UX_investment_proposal_benefits_proposal_sector");
    }
}

public sealed class InvestmentProposalLogframeConfiguration : IEntityTypeConfiguration<InvestmentProposalLogframe>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalLogframe> builder)
    {
        builder.ToTable("investment_proposal_logframe", t => t.HasCheckConstraint(
            "CK_investment_proposal_logframe_level",
            InvestmentProposalSql.InList("level", InvestmentProposalLogframeLevel.All)));

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.ProposalId).HasColumnName("proposal_id");
        builder.Property(l => l.Level).HasColumnName("level").IsRequired().HasMaxLength(10);
        builder.Property(l => l.Target).HasColumnName("target").HasMaxLength(4000);
        builder.Property(l => l.Verification).HasColumnName("verification").HasMaxLength(4000);

        builder.HasIndex(l => new { l.ProposalId, l.Level })
            .IsUnique()
            .HasDatabaseName("UX_investment_proposal_logframe_proposal_level");
    }
}

public sealed class InvestmentProposalGroupConfiguration : IEntityTypeConfiguration<InvestmentProposalGroup>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalGroup> builder)
    {
        builder.ToTable("investment_proposal_groups");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.ProposalId).HasColumnName("proposal_id");
        builder.Property(g => g.Label).HasColumnName("label").IsRequired().HasMaxLength(300);
        builder.Property(g => g.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(g => g.ProposalId).HasDatabaseName("IX_investment_proposal_groups_proposal_id");
    }
}

public sealed class InvestmentProposalWorkPlanRowConfiguration : IEntityTypeConfiguration<InvestmentProposalWorkPlanRow>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalWorkPlanRow> builder)
    {
        // An AIP row or a proposal-only step, never neither (spec §5).
        builder.ToTable("investment_proposal_work_plan_rows", t => t.HasCheckConstraint(
            "CK_investment_proposal_work_plan_rows_activity_or_name",
            "[aip_activity_id] IS NOT NULL OR [name] IS NOT NULL"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.ProposalId).HasColumnName("proposal_id");
        builder.Property(r => r.AipActivityId).HasColumnName("aip_activity_id");
        builder.Property(r => r.GroupId).HasColumnName("group_id");
        builder.Property(r => r.Name).HasColumnName("name").HasMaxLength(500);
        builder.Property(r => r.PerformanceTarget).HasColumnName("performance_target").HasMaxLength(4000);
        builder.Property(r => r.GenderIssues).HasColumnName("gender_issues").HasMaxLength(4000);
        builder.Property(r => r.Timeline).HasColumnName("timeline").HasMaxLength(200);
        builder.Property(r => r.Opr).HasColumnName("opr").HasMaxLength(300);
        builder.Property(r => r.SortOrder).HasColumnName("sort_order");

        // Each activity at most once per proposal. Filtered so any number of proposal-only rows
        // (null activity) can coexist. Also serves as the proposal_id FK index.
        builder.HasIndex(r => new { r.ProposalId, r.AipActivityId })
            .IsUnique()
            .HasFilter("[aip_activity_id] IS NOT NULL")
            .HasDatabaseName("UX_investment_proposal_work_plan_rows_proposal_activity");

        builder.HasIndex(r => r.AipActivityId).HasDatabaseName("IX_investment_proposal_work_plan_rows_aip_activity_id");
        builder.HasIndex(r => r.GroupId).HasDatabaseName("IX_investment_proposal_work_plan_rows_group_id");

        // CASCADE (decision 16): an activity deleted from the AIP takes its typed G text with it.
        builder.HasOne(r => r.AipActivity)
            .WithMany()
            .HasForeignKey(r => r.AipActivityId)
            .HasConstraintName("FK_investment_proposal_work_plan_rows_aip_activities_aip_activity_id")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // ⚠️ NO ACTION: rows are already reachable from investment_proposals directly, and SQL
        // Server allows only one cascade path. Clear group_id before deleting a group.
        builder.HasOne(r => r.Group)
            .WithMany()
            .HasForeignKey(r => r.GroupId)
            .HasConstraintName("FK_investment_proposal_work_plan_rows_investment_proposal_groups_group_id")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);
    }
}

public sealed class InvestmentProposalTeamMemberConfiguration : IEntityTypeConfiguration<InvestmentProposalTeamMember>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalTeamMember> builder)
    {
        builder.ToTable("investment_proposal_team_members", t => t.HasCheckConstraint(
            "CK_investment_proposal_team_members_sex",
            InvestmentProposalSql.InList("sex", InvestmentProposalSex.All)));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.ProposalId).HasColumnName("proposal_id");
        builder.Property(m => m.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
        builder.Property(m => m.Sex).HasColumnName("sex").IsRequired().HasMaxLength(1).IsFixedLength().IsUnicode(false);
        builder.Property(m => m.GadTrainings).HasColumnName("gad_trainings").HasMaxLength(1000);
        builder.Property(m => m.Expertise).HasColumnName("expertise").HasMaxLength(500);
        builder.Property(m => m.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(m => m.ProposalId).HasDatabaseName("IX_investment_proposal_team_members_proposal_id");
    }
}

public sealed class InvestmentProposalCapacityTrainingConfiguration : IEntityTypeConfiguration<InvestmentProposalCapacityTraining>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalCapacityTraining> builder)
    {
        builder.ToTable("investment_proposal_capacity_trainings");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.ProposalId).HasColumnName("proposal_id");
        builder.Property(c => c.MemberName).HasColumnName("member_name").IsRequired().HasMaxLength(200);
        builder.Property(c => c.Training).HasColumnName("training").HasMaxLength(1000);
        builder.Property(c => c.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(c => c.ProposalId).HasDatabaseName("IX_investment_proposal_capacity_trainings_proposal_id");
    }
}

public sealed class InvestmentProposalMonitoringConfiguration : IEntityTypeConfiguration<InvestmentProposalMonitoring>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalMonitoring> builder)
    {
        builder.ToTable("investment_proposal_monitoring", t => t.HasCheckConstraint(
            "CK_investment_proposal_monitoring_phase",
            InvestmentProposalSql.InList("phase", InvestmentProposalMonitoringPhase.All)));

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.ProposalId).HasColumnName("proposal_id");
        builder.Property(m => m.Phase).HasColumnName("phase").IsRequired().HasMaxLength(10);
        builder.Property(m => m.Activity).HasColumnName("activity").IsRequired().HasMaxLength(1000);
        builder.Property(m => m.Schedule).HasColumnName("schedule").HasMaxLength(500);
        builder.Property(m => m.Tools).HasColumnName("tools").HasMaxLength(1000);
        builder.Property(m => m.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(m => m.ProposalId).HasDatabaseName("IX_investment_proposal_monitoring_proposal_id");
    }
}

public sealed class InvestmentProposalRiskConfiguration : IEntityTypeConfiguration<InvestmentProposalRisk>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalRisk> builder)
    {
        builder.ToTable("investment_proposal_risks");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.ProposalId).HasColumnName("proposal_id");
        builder.Property(r => r.Risk).HasColumnName("risk").IsRequired().HasMaxLength(1000);
        builder.Property(r => r.Prevention).HasColumnName("prevention").HasMaxLength(2000);
        builder.Property(r => r.Monitoring).HasColumnName("monitoring").HasMaxLength(2000);
        builder.Property(r => r.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(r => r.ProposalId).HasDatabaseName("IX_investment_proposal_risks_proposal_id");
    }
}
