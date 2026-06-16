using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AipProgramConfiguration : IEntityTypeConfiguration<AipProgram>
{
    public void Configure(EntityTypeBuilder<AipProgram> builder)
    {
        builder.ToTable("aip_programs");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.OfficeId)
            .HasColumnName("office_id")
            .IsRequired();

        builder.Property(p => p.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired();  // nvarchar(max) — AIP names are unbounded free-text

        builder.HasIndex(p => new { p.OfficeId, p.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_aip_programs_office_id_ref_code");

        builder.HasIndex(p => p.OfficeId)
            .HasDatabaseName("IX_aip_programs_office_id");

        builder.HasIndex(p => p.RefCode)
            .HasDatabaseName("IX_aip_programs_ref_code");

        // Cascade: part of the AIP hierarchy chain (aip_records → … → aip_activities).
        builder.HasOne(p => p.Office)
            .WithMany(o => o.Programs)
            .HasForeignKey(p => p.OfficeId)
            .HasConstraintName("FK_aip_programs_aip_offices_office_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
