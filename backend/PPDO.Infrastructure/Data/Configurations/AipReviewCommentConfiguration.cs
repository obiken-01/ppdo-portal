using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// Mapping for <see cref="AipReviewComment"/> (V18-53 / PPDO-71, <c>AIP_Review_Spec.md</c> §5.1).
/// snake_case table and columns — a new table, so the new-table rule applies
/// (<c>docs/NAMING_CONVENTIONS.md</c>).
/// </summary>
public sealed class AipReviewCommentConfiguration : IEntityTypeConfiguration<AipReviewComment>
{
    public void Configure(EntityTypeBuilder<AipReviewComment> builder)
    {
        builder.ToTable("aip_review_comments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.AipOfficeId)
            .HasColumnName("aip_office_id")
            .IsRequired();

        // ⚠️ Stored as the enum NAME, not its int. These two columns are read by hand in SQL when
        // someone is working out why a comment could not be resolved, and "Ppdo" answers that
        // where "1" does not. Widths match the longest member with room to spare.
        builder.Property(e => e.NodeType)
            .HasColumnName("node_type")
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.NodeId)
            .HasColumnName("node_id")
            .IsRequired();

        builder.Property(e => e.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        builder.Property(e => e.AuthorSide)
            .HasColumnName("author_side")
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(16);

        builder.Property(e => e.Body)
            .HasColumnName("body")
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(e => e.ResolvedById)
            .HasColumnName("resolved_by_id");

        // IsUnresolved is computed from ResolvedAt in C#; it is not a column.
        builder.Ignore(e => e.IsUnresolved);

        // The unresolved count runs on every review page load and every re-submit, and it filters
        // on exactly this pair.
        builder.HasIndex(e => new { e.AipOfficeId, e.ResolvedAt })
            .HasDatabaseName("IX_aip_review_comments_office_resolved");

        builder.HasOne(e => e.AipOffice)
            .WithMany()
            .HasForeignKey(e => e.AipOfficeId)
            .HasConstraintName("FK_aip_review_comments_aip_offices_aip_office_id")
            .OnDelete(DeleteBehavior.Cascade);

        // ⚠️ Restrict on both user FKs, and NoAction is not enough on its own: two FKs into the
        // same Users table give SQL Server multiple cascade paths, which it refuses outright. A
        // user is deactivated rather than deleted here, so Restrict costs nothing and keeps the
        // authorship of a comment intact — "who asked for this change" is the point of the row.
        builder.HasOne(e => e.Author)
            .WithMany()
            .HasForeignKey(e => e.AuthorId)
            .HasConstraintName("FK_aip_review_comments_Users_author_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ResolvedBy)
            .WithMany()
            .HasForeignKey(e => e.ResolvedById)
            .HasConstraintName("FK_aip_review_comments_Users_resolved_by_id")
            .OnDelete(DeleteBehavior.Restrict);

        // ⚠️ No FK on (node_type, node_id) — a polymorphic anchor across three tables cannot have
        // one. See AipReviewComment.NodeId: a deleted node leaves the comment orphaned on purpose,
        // and the read tolerates it rather than the database removing the evidence.
    }
}
