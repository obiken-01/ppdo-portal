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
