using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AipRecordConfiguration : IEntityTypeConfiguration<AipRecord>
{
    public void Configure(EntityTypeBuilder<AipRecord> builder)
    {
        builder.ToTable("aip_records");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.FiscalYear)
            .HasColumnName("fiscal_year")
            .IsRequired();

        builder.Property(a => a.EntrySource)
            .HasColumnName("entry_source")
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(a => a.OriginalFilename)
            .HasColumnName("original_filename")
            .HasMaxLength(500);

        builder.Property(a => a.UploadedById)
            .HasColumnName("uploaded_by")
            .IsRequired();

        builder.Property(a => a.UploadedAt)
            .HasColumnName("uploaded_at")
            .HasDefaultValueSql("GETUTCDATE()");

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Draft");

        builder.Property(a => a.LdipId)
            .HasColumnName("ldip_id");

        builder.Property(a => a.SourceId)
            .HasColumnName("source_id");

        // Source chain index (amendment/supplemental tracing).
        builder.HasIndex(a => a.SourceId)
            .HasDatabaseName("IX_aip_source_id");

        // Restrict: never delete a user who has uploaded planning records.
        builder.HasOne(a => a.UploadedBy)
            .WithMany()
            .HasForeignKey(a => a.UploadedById)
            .HasConstraintName("FK_aip_records_Users_uploaded_by")
            .OnDelete(DeleteBehavior.Restrict);

        // Optional future link to LDIP — Restrict: LDIP cannot be deleted while referenced.
        builder.HasOne(a => a.Ldip)
            .WithMany()
            .HasForeignKey(a => a.LdipId)
            .HasConstraintName("FK_aip_records_ldip_records_ldip_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Self-FK — Restrict: a record cannot be deleted while a copy points at it.
        builder.HasOne(a => a.Source)
            .WithMany()
            .HasForeignKey(a => a.SourceId)
            .HasConstraintName("FK_aip_records_aip_records_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
