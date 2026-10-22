using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AipOfficeConfiguration : IEntityTypeConfiguration<AipOffice>
{
    public void Configure(EntityTypeBuilder<AipOffice> builder)
    {
        builder.ToTable("aip_offices");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.AipRecordId)
            .HasColumnName("aip_record_id")
            .IsRequired();

        builder.Property(o => o.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .IsRequired();  // nvarchar(max) — AIP names are unbounded free-text

        builder.Property(o => o.Sector)
            .HasColumnName("sector")
            .IsRequired()
            .HasMaxLength(20);

        // NOT unique: the AIP file legitimately has multiple office-level rows with
        // the same ref code (main office + sub-offices share the same 5-segment code).
        builder.HasIndex(o => new { o.AipRecordId, o.RefCode })
            .HasDatabaseName("IX_aip_offices_aip_record_id_ref_code");

        builder.HasIndex(o => o.AipRecordId)
            .HasDatabaseName("IX_aip_offices_aip_record_id");

        builder.HasIndex(o => o.RefCode)
            .HasDatabaseName("IX_aip_offices_ref_code");

        builder.Property(o => o.OfficeId)
            .HasColumnName("office_id");

        // The read path since V18-32. Every scoped AIP read is "this record's offices, owned by
        // this office" — the same shape the suffix match used to serve, now indexable.
        builder.HasIndex(o => new { o.AipRecordId, o.OfficeId })
            .HasDatabaseName("IX_aip_offices_aip_record_id_office_id");

        // V18-42 / PPDO-52 — one office's position in the review workflow (spec §5.1).
        //
        // ⚠️ NVARCHAR(30), not an enum column and not the 20 that aip_records.status uses:
        // "DepartmentReview" and "SubmittedToPpdo" are 16 characters, so 20 leaves almost no room
        // for a sixth state and the column would need widening the first time Phase 4 adds one.
        //
        // ⚠️ Defaulted in SQL as well as in the entity. Existing rows predate this column and must
        // land on Draft without a backfill statement; a default only in C# would leave them empty
        // strings, which is not one of the five states and would fail every read that switches on
        // it.
        builder.Property(o => o.WorkflowStatus)
            .HasColumnName("workflow_status")
            .IsRequired()
            .HasMaxLength(30)
            .HasDefaultValue("Draft");

        // (office_id, workflow_status) — the Phase 4 review queue's read: "every office of mine
        // sitting in this state". Leading with office_id rather than the status keeps it useful
        // for the single-office lookup this phase makes, too.
        builder.HasIndex(o => new { o.OfficeId, o.WorkflowStatus })
            .HasDatabaseName("IX_aip_offices_workflow_status");

        // ── SAIP working copy (v1.10.0 — PPDO-246, SAIP_Spec.md §5) ───────────────
        //
        // A draft copy is a second set of group rows in the same record, carrying saip_id. The
        // approved rows keep saip_id NULL.
        builder.Property(o => o.SaipId).HasColumnName("saip_id");
        builder.Property(o => o.SourceId).HasColumnName("source_id");

        builder.HasIndex(o => o.SaipId)
            .HasDatabaseName("IX_aip_offices_saip_id");

        // NO ACTION: an SAIP row is never deleted (a cancelled one stays in the history), and its
        // copy is removed by SaipRepository before the SAIP is closed.
        builder.HasOne<AipSaip>()
            .WithMany()
            .HasForeignKey(o => o.SaipId)
            .HasConstraintName("FK_aip_offices_aip_saips_saip_id")
            .OnDelete(DeleteBehavior.NoAction)
            .IsRequired(false);

        // ⚠️ THE GUARD. Every existing read of aip_offices — AIP Entry, the review list, Annex B,
        // the initial WFP, the dashboard, the ceiling sums, the GSO feed — skips draft copies, so no
        // report can pick up unapproved figures by accident. Reads that start from a child table
        // and join up through Office (e.g. e.Activity.Project.Program.Office.AipRecordId) are
        // filtered too: EF applies this filter to the navigation join. Reads keyed on a parent id
        // are filtered by construction, because the parent ids come from a filtered office read.
        //
        // Only SAIP code sees copies, through SaipRepository and IgnoreQueryFilters(). Do not add
        // IgnoreQueryFilters() anywhere else. SaipCopyQueryFilterTests pins this.
        builder.HasQueryFilter(o => o.SaipId == null);

        // Cascade: deleting an AIP record removes its entire hierarchy.
        builder.HasOne(o => o.AipRecord)
            .WithMany(r => r.Offices)
            .HasForeignKey(o => o.AipRecordId)
            .HasConstraintName("FK_aip_offices_aip_records_aip_record_id")
            .OnDelete(DeleteBehavior.Cascade);

        // V18-32 — ownership is a real FK. Restrict, not Cascade, and the contrast with the line
        // above is deliberate: deleting an AIP RECORD should take its hierarchy with it, but
        // deleting a config OFFICE must never silently delete a fiscal year of that office's AIP.
        builder.HasOne(o => o.Office)
            .WithMany()
            .HasForeignKey(o => o.OfficeId)
            .HasConstraintName("FK_aip_offices_offices_office_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
