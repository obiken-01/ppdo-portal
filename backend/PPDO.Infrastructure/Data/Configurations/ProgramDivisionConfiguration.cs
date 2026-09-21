using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class ProgramDivisionConfiguration : IEntityTypeConfiguration<ProgramDivision>
{
    public void Configure(EntityTypeBuilder<ProgramDivision> builder)
    {
        builder.ToTable("program_divisions");

        builder.HasKey(pd => pd.Id);
        builder.Property(pd => pd.Id).HasColumnName("id");

        builder.Property(pd => pd.OfficeRefCode)
            .HasColumnName("office_ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(pd => pd.OfficeId)
            .HasColumnName("office_id");

        builder.Property(pd => pd.ProgramRefCode)
            .HasColumnName("program_ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(pd => pd.DivisionId)
            .HasColumnName("division_id")
            .IsRequired();

        // Composite unique — one division appears at most once per (office, program).
        builder.HasIndex(pd => new { pd.OfficeRefCode, pd.ProgramRefCode, pd.DivisionId })
            .IsUnique()
            .HasDatabaseName("IX_program_divisions_ref_div");

        // Read path since RAL-249: (office_id, program_ref_code). Not unique — a program may be
        // assigned to several divisions; the uniqueness rule lives on the index above.
        builder.HasIndex(pd => new { pd.OfficeId, pd.ProgramRefCode })
            .HasDatabaseName("IX_program_divisions_office_program");

        builder.HasOne(pd => pd.Division)
            .WithMany()
            .HasForeignKey(pd => pd.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);

        // RAL-249 — the office side is a real FK: a config offices row is stable across fiscal
        // years and re-uploads. Restrict, not Cascade: deleting an office must not silently take
        // its division assignments with it.
        builder.HasOne(pd => pd.Office)
            .WithMany()
            .HasForeignKey(pd => pd.OfficeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ⚠️ Still deliberately NO FK to aip_programs — those rows are recreated with new
        // surrogate IDs by every re-upload and do not survive a fiscal year. See ProgramDivision's
        // remarks before adding one.
    }
}
