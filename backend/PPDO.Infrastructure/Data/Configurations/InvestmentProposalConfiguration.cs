using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// snake_case mapping for <see cref="InvestmentProposal"/> (PPDO-154). Spec:
/// <c>docs/v1.8/Investment_Proposal_Spec.md</c> §5.
/// </summary>
public sealed class InvestmentProposalConfiguration : IEntityTypeConfiguration<InvestmentProposal>
{
    public void Configure(EntityTypeBuilder<InvestmentProposal> builder)
    {
        builder.ToTable("investment_proposals", t => t.HasCheckConstraint(
            "CK_investment_proposals_status",
            InvestmentProposalSql.InList("status", InvestmentProposalStatus.All)));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.AipProjectId).HasColumnName("aip_project_id").IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue(InvestmentProposalStatus.Draft);

        builder.Property(p => p.ProjectLocation).HasColumnName("project_location").HasMaxLength(500);
        builder.Property(p => p.HgdgChecklist).HasColumnName("hgdg_checklist").HasMaxLength(50);
        builder.Property(p => p.HgdgScore).HasColumnName("hgdg_score").HasPrecision(3, 1);

        // nvarchar(max): sanitized HTML, capped at 100,000 chars by the validator (PPDO-155).
        builder.Property(p => p.Description).HasColumnName("description");
        builder.Property(p => p.Rationale).HasColumnName("rationale");
        builder.Property(p => p.GeneralObjective).HasColumnName("general_objective");
        builder.Property(p => p.PartnershipSustainability).HasColumnName("partnership_sustainability");

        builder.Property(p => p.WomensImpactStrategy).HasColumnName("womens_impact_strategy").HasMaxLength(4000);
        builder.Property(p => p.ProjectSupervisor).HasColumnName("project_supervisor").HasMaxLength(200);
        builder.Property(p => p.ProjectManager).HasColumnName("project_manager").HasMaxLength(200);

        // Default 1 (decision 10). EF 9 makes the sentinel the default value (true), so an explicit
        // false is still written. Under a false sentinel, unticking "Same as Section A" on a new
        // proposal would come back ticked. Pinned by
        // InvestmentProposalRepositoryTests.Save_DirectSameAsSummaryFalse_IsNotOverwrittenByTheColumnDefault.
        builder.Property(p => p.DirectSameAsSummary)
            .HasColumnName("direct_same_as_summary")
            .HasDefaultValue(true);

        builder.Property(p => p.Signatory1Label).HasColumnName("signatory1_label").HasMaxLength(100);
        builder.Property(p => p.Signatory1Name).HasColumnName("signatory1_name").HasMaxLength(200);
        builder.Property(p => p.Signatory1Position).HasColumnName("signatory1_position").HasMaxLength(200);
        builder.Property(p => p.Signatory2Label).HasColumnName("signatory2_label").HasMaxLength(100);
        builder.Property(p => p.Signatory2Name).HasColumnName("signatory2_name").HasMaxLength(200);
        builder.Property(p => p.Signatory2Position).HasColumnName("signatory2_position").HasMaxLength(200);
        builder.Property(p => p.Signatory3Label).HasColumnName("signatory3_label").HasMaxLength(100);
        builder.Property(p => p.Signatory3Name).HasColumnName("signatory3_name").HasMaxLength(200);
        builder.Property(p => p.Signatory3Position).HasColumnName("signatory3_position").HasMaxLength(200);

        builder.Property(p => p.SnapshotJson).HasColumnName("snapshot_json");
        builder.Property(p => p.FinalizedAt).HasColumnName("finalized_at");
        builder.Property(p => p.FinalizedById).HasColumnName("finalized_by_user_id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(p => p.CreatedById).HasColumnName("created_by_user_id");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(p => p.UpdatedById).HasColumnName("updated_by_user_id");

        // The whole proposal is the unit of conflict (decision 23).
        builder.Property(p => p.RowVersion).HasColumnName("row_version").IsRowVersion();

        // One proposal per project (decision 1). Also the list query's join index.
        builder.HasIndex(p => p.AipProjectId)
            .IsUnique()
            .HasDatabaseName("UX_investment_proposals_aip_project_id");

        // NO ACTION (decision 26): deleting a project that has a proposal must fail, not cascade
        // away someone's narrative. AipService turns it into a 409 before it reaches the database.
        builder.HasOne(p => p.AipProject)
            .WithMany()
            .HasForeignKey(p => p.AipProjectId)
            .HasConstraintName("FK_investment_proposals_aip_projects_aip_project_id")
            .OnDelete(DeleteBehavior.NoAction);

        // ⚠️ Restrict, not the spec's SET NULL. Three SET NULL paths from users into one table is
        // a "multiple cascade paths" error in SQL Server, the same reason aip_division_submissions
        // uses Restrict on its two user FKs. Users are deactivated, never hard-deleted.
        builder.HasOne(p => p.FinalizedBy)
            .WithMany()
            .HasForeignKey(p => p.FinalizedById)
            .HasConstraintName("FK_investment_proposals_users_finalized_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(p => p.CreatedBy)
            .WithMany()
            .HasForeignKey(p => p.CreatedById)
            .HasConstraintName("FK_investment_proposals_users_created_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(p => p.UpdatedBy)
            .WithMany()
            .HasForeignKey(p => p.UpdatedById)
            .HasConstraintName("FK_investment_proposals_users_updated_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Children: all cascade from the proposal (spec §5).
        builder.HasMany(p => p.Beneficiaries).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_beneficiaries_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Benefits).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_benefits_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Logframe).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_logframe_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Groups).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_groups_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.WorkPlanRows).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_work_plan_rows_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.TeamMembers).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_team_members_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.CapacityTrainings).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_capacity_trainings_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Monitoring).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_monitoring_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Risks).WithOne().HasForeignKey(c => c.ProposalId)
            .HasConstraintName("FK_investment_proposal_risks_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Builds the <c>[col] IN (...)</c> CHECK body from a constants list.</summary>
internal static class InvestmentProposalSql
{
    public static string InList(string column, IReadOnlyList<string> values)
        => $"[{column}] IN ({string.Join(", ", values.Select(v => $"'{v}'"))})";
}
