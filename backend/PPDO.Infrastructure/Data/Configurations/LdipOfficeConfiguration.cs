using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class LdipOfficeConfiguration : IEntityTypeConfiguration<LdipOffice>
{
    public void Configure(EntityTypeBuilder<LdipOffice> builder)
    {
        builder.ToTable("ldip_offices");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.LdipRecordId)
            .HasColumnName("ldip_record_id")
            .IsRequired();

        builder.Property(o => o.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(o => o.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.Sector)
            .HasColumnName("sector")
            .IsRequired()
            .HasMaxLength(20);

        // Unique: the record's office is fixed, so one row per sector ref code.
        builder.HasIndex(o => new { o.LdipRecordId, o.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_ldip_offices_ldip_record_id_ref_code");

        builder.HasIndex(o => o.LdipRecordId)
            .HasDatabaseName("IX_ldip_offices_ldip_record_id");

        // Cascade: deleting an LDIP record removes its entire hierarchy.
        builder.HasOne(o => o.LdipRecord)
            .WithMany(r => r.Offices)
            .HasForeignKey(o => o.LdipRecordId)
            .HasConstraintName("FK_ldip_offices_ldip_records_ldip_record_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
