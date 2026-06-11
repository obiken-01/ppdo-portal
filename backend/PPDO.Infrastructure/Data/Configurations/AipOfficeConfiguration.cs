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
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.Sector)
            .HasColumnName("sector")
            .IsRequired()
            .HasMaxLength(20);

        builder.HasIndex(o => new { o.AipRecordId, o.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_aip_offices_aip_record_id_ref_code");

        builder.HasIndex(o => o.AipRecordId)
            .HasDatabaseName("IX_aip_offices_aip_record_id");

        builder.HasIndex(o => o.RefCode)
            .HasDatabaseName("IX_aip_offices_ref_code");

        // Cascade: deleting an AIP record removes its entire hierarchy.
        builder.HasOne(o => o.AipRecord)
            .WithMany(r => r.Offices)
            .HasForeignKey(o => o.AipRecordId)
            .HasConstraintName("FK_aip_offices_aip_records_aip_record_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
