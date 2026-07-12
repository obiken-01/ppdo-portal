using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;

namespace PPDO.Infrastructure.Data.Configurations;

public sealed class AipActivityConfiguration : IEntityTypeConfiguration<AipActivity>
{
    public void Configure(EntityTypeBuilder<AipActivity> builder)
    {
        builder.ToTable("aip_activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        builder.Property(a => a.RefCode)
            .HasColumnName("ref_code")
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(a => a.Name)
            .HasColumnName("name")
            .IsRequired();  // nvarchar(max) — AIP names are unbounded free-text

        builder.Property(a => a.EsreCode)
            .HasColumnName("esre_code")
            .HasMaxLength(20);

        builder.Property(a => a.ImplementingOffice)
            .HasColumnName("implementing_office");  // nvarchar(max) — sub-office names are unbounded

        // Stored as strings — source data uses month names ("January"), not dates.
        builder.Property(a => a.StartDate)
            .HasColumnName("start_date")
            .HasMaxLength(50);

        builder.Property(a => a.EndDate)
            .HasColumnName("end_date")
            .HasMaxLength(50);

        builder.Property(a => a.ExpectedOutputs)
            .HasColumnName("expected_outputs");  // nvarchar(max), nullable

        builder.Property(a => a.FundingSourceId)
            .HasColumnName("funding_source_id");

        // nvarchar(max) — AIP activities can list multiple funding sources separated by "/";
        // WFP picks one at entry time. Store the full raw value here.
        builder.Property(a => a.FundingSourceSnapshot)
            .HasColumnName("funding_source_snapshot");

        builder.Property(a => a.Ps)
            .HasColumnName("ps")
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.Mooe)
            .HasColumnName("mooe")
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.Co)
            .HasColumnName("co")
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.Total)
            .HasColumnName("total")
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.CcAdaptation)
            .HasColumnName("cc_adaptation")
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.CcMitigation)
            .HasColumnName("cc_mitigation")
            .HasColumnType("decimal(18,2)");

        builder.Property(a => a.CcTypologyCode)
            .HasColumnName("cc_typology_code")
            .HasMaxLength(50);

        builder.Property(a => a.IsCreation)
            .HasColumnName("is_creation")
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(a => new { a.ProjectId, a.RefCode })
            .IsUnique()
            .HasDatabaseName("UX_aip_activities_project_id_ref_code");

        builder.HasIndex(a => a.ProjectId)
            .HasDatabaseName("IX_aip_activities_project_id");

        builder.HasIndex(a => a.RefCode)
            .HasDatabaseName("IX_aip_activities_ref_code");

        // Cascade: leaf of the AIP hierarchy chain (aip_records → … → aip_activities).
        builder.HasOne(a => a.Project)
            .WithMany(p => p.Activities)
            .HasForeignKey(a => a.ProjectId)
            .HasConstraintName("FK_aip_activities_aip_projects_project_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: config rows are soft-deleted (is_active), never hard-deleted while referenced.
        builder.HasOne(a => a.FundingSource)
            .WithMany()
            .HasForeignKey(a => a.FundingSourceId)
            .HasConstraintName("FK_aip_activities_funding_sources_funding_source_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
