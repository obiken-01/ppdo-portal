using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class WfpRecordConfiguration : IEntityTypeConfiguration<WfpRecord>
{
    public void Configure(EntityTypeBuilder<WfpRecord> builder)
    {
        builder.ToTable("wfp_records");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");

        builder.Property(w => w.AipRecordId)
            .HasColumnName("aip_record_id")
            .IsRequired();

        builder.Property(w => w.OfficeId)
            .HasColumnName("office_id")
            .IsRequired();

        builder.Property(w => w.DivisionId)
            .HasColumnName("division_id");

        builder.Property(w => w.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(w => w.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Draft");

        builder.Property(w => w.CreatedById)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(w => w.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(w => w.FinalizedAt)
            .HasColumnName("finalized_at");

        builder.Property(w => w.SourceId)
            .HasColumnName("source_id");

        // 1 WFP per (aip, office, division) triplet.
        builder.HasIndex(w => new { w.AipRecordId, w.OfficeId, w.DivisionId })
            .IsUnique()
            .HasDatabaseName("UX_wfp_records_aip_office_division");

        builder.HasIndex(w => w.AipRecordId)
            .HasDatabaseName("IX_wfp_records_aip_record_id");

        builder.HasIndex(w => w.OfficeId)
            .HasDatabaseName("IX_wfp_records_office_id");

        builder.HasIndex(w => w.DivisionId)
            .HasDatabaseName("IX_wfp_records_division_id");

        // Source chain index (amendment/supplemental tracing).
        builder.HasIndex(w => w.SourceId)
            .HasDatabaseName("IX_wfp_source_id");

        // Restrict: an AIP record cannot be deleted while WFPs are built from it.
        builder.HasOne(w => w.AipRecord)
            .WithMany(a => a.WfpRecords)
            .HasForeignKey(w => w.AipRecordId)
            .HasConstraintName("FK_wfp_records_aip_records_aip_record_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(w => w.Office)
            .WithMany(o => o.WfpRecords)
            .HasForeignKey(w => w.OfficeId)
            .HasConstraintName("FK_wfp_records_offices_office_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: a division cannot be deleted while WFPs reference it.
        builder.HasOne(w => w.Division)
            .WithMany()
            .HasForeignKey(w => w.DivisionId)
            .HasConstraintName("FK_wfp_records_divisions_division_id")
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: never delete a user who has authored planning records.
        builder.HasOne(w => w.CreatedBy)
            .WithMany()
            .HasForeignKey(w => w.CreatedById)
            .HasConstraintName("FK_wfp_records_Users_created_by")
            .OnDelete(DeleteBehavior.Restrict);

        // Self-FK — Restrict: a record cannot be deleted while a copy points at it.
        builder.HasOne(w => w.Source)
            .WithMany()
            .HasForeignKey(w => w.SourceId)
            .HasConstraintName("FK_wfp_records_wfp_records_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
