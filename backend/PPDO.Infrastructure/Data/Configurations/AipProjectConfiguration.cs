using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AipProjectConfiguration : IEntityTypeConfiguration<AipProject>
{
    public void Configure(EntityTypeBuilder<AipProject> builder)
    {
        builder.ToTable("aip_projects");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.ProgramId)
            .HasColumnName("program_id")
            .IsRequired();

        builder.Property(p => p.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired();  // nvarchar(max) — AIP names are unbounded free-text

        builder.Property(p => p.IsSynthetic)
            .HasColumnName("is_synthetic")
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(p => new { p.ProgramId, p.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_aip_projects_program_id_ref_code");

        builder.HasIndex(p => p.ProgramId)
            .HasDatabaseName("IX_aip_projects_program_id");

        builder.HasIndex(p => p.RefCode)
            .HasDatabaseName("IX_aip_projects_ref_code");

        // Cascade: part of the AIP hierarchy chain (aip_records → … → aip_activities).
        builder.HasOne(p => p.Program)
            .WithMany(pr => pr.Projects)
            .HasForeignKey(p => p.ProgramId)
            .HasConstraintName("FK_aip_projects_aip_programs_program_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
