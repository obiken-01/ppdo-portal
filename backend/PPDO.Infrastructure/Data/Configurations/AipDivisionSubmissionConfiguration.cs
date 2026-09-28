using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// snake_case mapping for <see cref="AipDivisionSubmission"/> (PPDO-130). It is a new table, so it
/// uses snake_case columns per docs/NAMING_CONVENTIONS.md. Spec:
/// <c>docs/v1.8/Division_Submit_Spec.md</c> §5.
/// </summary>
public sealed class AipDivisionSubmissionConfiguration : IEntityTypeConfiguration<AipDivisionSubmission>
{
    public void Configure(EntityTypeBuilder<AipDivisionSubmission> builder)
    {
        // The CHECK is built from AipDivisionStatus.All so the column's domain and the constants
        // cannot drift. A value outside it fails at the write rather than surfacing as an unknown
        // state on a later read.
        builder.ToTable("aip_division_submissions", t => t.HasCheckConstraint(
            "CK_aip_division_submissions_status",
            $"[status] IN ({string.Join(", ", AipDivisionStatus.All.Select(s => $"'{s}'"))})"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.AipRecordId)
            .HasColumnName("aip_record_id")
            .IsRequired();

        builder.Property(s => s.OfficeId)
            .HasColumnName("office_id")
            .IsRequired();

        builder.Property(s => s.DivisionId)
            .HasColumnName("division_id")
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue(AipDivisionStatus.Draft);

        builder.Property(s => s.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(s => s.SubmittedById).HasColumnName("submitted_by_user_id");
        builder.Property(s => s.ReturnedAt).HasColumnName("returned_at");
        builder.Property(s => s.ReturnedById).HasColumnName("returned_by_user_id");

        // One row per division per fiscal year. This is what makes a double submit an update of
        // the same row rather than a second row that disagrees with the first.
        builder.HasIndex(s => new { s.AipRecordId, s.DivisionId })
            .IsUnique()
            .HasDatabaseName("UX_aip_division_submissions_record_division");

        // The read path: every division of one office in one record, in one query (spec §5 —
        // "one query per office, never per division").
        builder.HasIndex(s => new { s.AipRecordId, s.OfficeId })
            .HasDatabaseName("IX_aip_division_submissions_record_office");

        // Cascade: the rows describe the record's workflow and mean nothing without it, exactly
        // like the aip_offices hierarchy under it.
        builder.HasOne(s => s.AipRecord)
            .WithMany()
            .HasForeignKey(s => s.AipRecordId)
            .HasConstraintName("FK_aip_division_submissions_aip_records_aip_record_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: config rows are soft-deleted and never hard-deleted while referenced.
        builder.HasOne(s => s.Office)
            .WithMany()
            .HasForeignKey(s => s.OfficeId)
            .HasConstraintName("FK_aip_division_submissions_offices_office_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Division)
            .WithMany()
            .HasForeignKey(s => s.DivisionId)
            .HasConstraintName("FK_aip_division_submissions_divisions_division_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict on the two user FKs, the same as the other AIP workflow actors
        // (aip_review_comments.resolved_by, aip_records.uploaded_by). Users are deactivated
        // (is_active), never hard-deleted, and SET NULL on two paths from Users into one table is
        // refused by SQL Server ("may cause cycles or multiple cascade paths").
        builder.HasOne(s => s.SubmittedBy)
            .WithMany()
            .HasForeignKey(s => s.SubmittedById)
            .HasConstraintName("FK_aip_division_submissions_users_submitted_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne(s => s.ReturnedBy)
            .WithMany()
            .HasForeignKey(s => s.ReturnedById)
            .HasConstraintName("FK_aip_division_submissions_users_returned_by_user_id")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
