using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class LdipProgramConfiguration : IEntityTypeConfiguration<LdipProgram>
{
    public void Configure(EntityTypeBuilder<LdipProgram> builder)
    {
        builder.ToTable("ldip_programs");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.LdipOfficeId)
            .HasColumnName("ldip_office_id")
            .IsRequired();

        builder.Property(p => p.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired()
            .HasMaxLength(500);

        // Stored in thousands (₱000), like AIP totals.
        builder.Property(p => p.Budget)
            .HasColumnName("budget")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(p => new { p.LdipOfficeId, p.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_ldip_programs_ldip_office_id_ref_code");

        builder.HasIndex(p => p.LdipOfficeId)
            .HasDatabaseName("IX_ldip_programs_ldip_office_id");

        // Cascade: part of the LDIP hierarchy chain (ldip_records → ldip_offices → ldip_programs).
        builder.HasOne(p => p.Office)
            .WithMany(o => o.Programs)
            .HasForeignKey(p => p.LdipOfficeId)
            .HasConstraintName("FK_ldip_programs_ldip_offices_ldip_office_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
