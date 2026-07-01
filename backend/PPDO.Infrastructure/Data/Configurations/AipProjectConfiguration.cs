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

        // RAL-108: mirrors AipActivityConfiguration — all nullable, populated only when a
        // project row carries its own line-item detail with no child activity.
        builder.Property(p => p.EsreCode)
            .HasColumnName("esre_code")
            .HasMaxLength(20);

        builder.Property(p => p.ImplementingOffice)
            .HasColumnName("implementing_office");

        builder.Property(p => p.StartDate)
            .HasColumnName("start_date")
            .HasMaxLength(50);

        builder.Property(p => p.EndDate)
            .HasColumnName("end_date")
            .HasMaxLength(50);

        builder.Property(p => p.ExpectedOutputs)
            .HasColumnName("expected_outputs");

        builder.Property(p => p.FundingSourceId)
            .HasColumnName("funding_source_id");

        builder.Property(p => p.FundingSourceSnapshot)
            .HasColumnName("funding_source_snapshot");

        builder.Property(p => p.Ps)
            .HasColumnName("ps")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Mooe)
            .HasColumnName("mooe")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Co)
            .HasColumnName("co")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.Total)
            .HasColumnName("total")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CcAdaptation)
            .HasColumnName("cc_adaptation")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CcMitigation)
            .HasColumnName("cc_mitigation")
            .HasColumnType("decimal(18,2)");

        builder.Property(p => p.CcTypologyCode)
            .HasColumnName("cc_typology_code")
            .HasMaxLength(50);

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

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(p => p.FundingSource)
            .WithMany()
            .HasForeignKey(p => p.FundingSourceId)
            .HasConstraintName("FK_aip_projects_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
