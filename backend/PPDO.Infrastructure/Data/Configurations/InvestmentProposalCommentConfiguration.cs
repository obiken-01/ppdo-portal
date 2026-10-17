using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping for <see cref="InvestmentProposalComment"/> (v1.9.0 — PPDO-219). snake_case: a new table.
/// </summary>
public sealed class InvestmentProposalCommentConfiguration : IEntityTypeConfiguration<InvestmentProposalComment>
{
    public void Configure(EntityTypeBuilder<InvestmentProposalComment> builder)
    {
        builder.ToTable("investment_proposal_comments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProposalId).HasColumnName("proposal_id").IsRequired();
        builder.Property(e => e.ParentCommentId).HasColumnName("parent_comment_id");
        builder.Property(e => e.Body).HasColumnName("body").IsRequired().HasMaxLength(2000);

        // Stored as the member name so a hand-run query reads "Ppdo", not 1.
        builder.Property(e => e.AuthorSide)
            .HasColumnName("author_side").HasConversion<string>().IsRequired().HasMaxLength(10);

        builder.Property(e => e.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(e => e.ResolvedById).HasColumnName("resolved_by_id");

        builder.Ignore(e => e.IsOpen);
        builder.Ignore(e => e.IsHead);

        // Serves the proposal_id FK, the thread read, and the open-count (Finalize gate, list pill).
        builder.HasIndex(e => new { e.ProposalId, e.ResolvedAt })
            .HasDatabaseName("IX_investment_proposal_comments_proposal_resolved");

        // Deleting a Draft proposal deletes its comments (spec decision 8).
        builder.HasOne(e => e.Proposal)
            .WithMany()
            .HasForeignKey(e => e.ProposalId)
            .HasConstraintName("FK_investment_proposal_comments_investment_proposals_proposal_id")
            .OnDelete(DeleteBehavior.Cascade);

        // A reply points at its head. NO ACTION (SQL Server refuses cascade on a self-reference); the
        // proposal's cascade still removes head and replies in the same delete.
        builder.HasOne(e => e.Parent)
            .WithMany()
            .HasForeignKey(e => e.ParentCommentId)
            .HasConstraintName("FK_investment_proposal_comments_investment_proposal_comments_parent_comment_id")
            .OnDelete(DeleteBehavior.NoAction);

        // Restrict on both user FKs: two FKs into Users would otherwise be multiple cascade paths,
        // which SQL Server refuses. Users are deactivated, not deleted.
        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .HasConstraintName("FK_investment_proposal_comments_Users_author_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ResolvedBy)
            .WithMany()
            .HasForeignKey(e => e.ResolvedById)
            .HasConstraintName("FK_investment_proposal_comments_Users_resolved_by_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
