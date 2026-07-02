using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class LdipRecordConfiguration : IEntityTypeConfiguration<LdipRecord>
{
    public void Configure(EntityTypeBuilder<LdipRecord> builder)
    {
        builder.ToTable("ldip_records");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        // Nullable for pre-v1.3 rows; required by the service for new records (RAL-61).
        builder.Property(l => l.OfficeId)
            .HasColumnName("office_id");

        builder.Property(l => l.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(l => l.Title)
            .HasColumnName("title")
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(l => l.FiscalYearStart)
            .HasColumnName("fiscal_year_start")
            .IsRequired();

        builder.Property(l => l.FiscalYearEnd)
            .HasColumnName("fiscal_year_end")
            .IsRequired();

        builder.Property(l => l.EntryMode)
            .HasColumnName("entry_mode")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(l => l.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Draft");

        builder.Property(l => l.SourceId)
            .HasColumnName("source_id");

        builder.Property(l => l.CreatedById)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.HasIndex(l => l.RefCode)
            .IsUnique()
            .HasDatabaseName("IX_ldip_records_ref_code");

        // Source chain index (amendment/supplemental tracing).
        builder.HasIndex(l => l.SourceId)
            .HasDatabaseName("IX_ldip_source_id");

        // Self-FK — Restrict: a record cannot be deleted while a copy points at it.
        builder.HasOne(l => l.Source)
            .WithMany()
            .HasForeignKey(l => l.SourceId)
            .HasConstraintName("FK_ldip_records_ldip_records_source_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: never delete a user who has authored planning records.
        builder.HasOne(l => l.CreatedBy)
            .WithMany()
            .HasForeignKey(l => l.CreatedById)
            .HasConstraintName("FK_ldip_records_Users_created_by")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.OfficeId)
            .HasDatabaseName("IX_ldip_records_office_id");

        // Restrict: never delete a config office that has LDIP documents.
        builder.HasOne(l => l.Office)
            .WithMany()
            .HasForeignKey(l => l.OfficeId)
            .HasConstraintName("FK_ldip_records_offices_office_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
