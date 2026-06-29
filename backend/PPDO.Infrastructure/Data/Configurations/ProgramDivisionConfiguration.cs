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

        // FK to divisions only — deliberately NO FK to aip_programs.
        // Assignments survive supplemental AIP re-uploads because they key on ref codes (D6).
        builder.HasOne(pd => pd.Division)
            .WithMany()
            .HasForeignKey(pd => pd.DivisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
