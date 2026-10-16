using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Infrastructure.Data.Configurations;

/// <summary>
/// <c>initial_wfp_export_jobs</c> (v1.9.0 — PPDO-228, spec §5): the <see cref="BackgroundJob"/> columns,
/// configured in <see cref="BackgroundJobConfiguration"/>, plus the export's own.
/// </summary>
public sealed class InitialWfpExportJobConfiguration : IEntityTypeConfiguration<InitialWfpExportJob>
{
    public const string TableName = "initial_wfp_export_jobs";

    public void Configure(EntityTypeBuilder<InitialWfpExportJob> builder)
    {
        builder.ToTable(TableName);

        builder.Property(e => e.FiscalYear).HasColumnName("fiscal_year").IsRequired();
        builder.Property(e => e.DoneCount).HasColumnName("done_count").HasDefaultValue(0);
        builder.Property(e => e.TotalCount).HasColumnName("total_count").HasDefaultValue(0);
        builder.Property(e => e.ProblemsJson).HasColumnName("problems_json");
        builder.Property(e => e.FileKey).HasColumnName("file_key").HasMaxLength(400);
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at");

        // "The latest / the active job for this year" (spec §5).
        builder.HasIndex(e => new { e.FiscalYear, e.Status })
            .HasDatabaseName("IX_initial_wfp_export_jobs_fiscal_year_status");

        // The claim: Queued, oldest first, read in this index's order so the claim touches (and locks)
        // only the row it takes. Also serves the stuck-job query's status seek.
        // ↩️ Spec §5 said (status, claimed_at). The claim orders by created_at, and without an index in
        // that order SQL Server sorts every Queued row first, holding an update lock on each of them,
        // so a second claimer at that moment found them all locked and claimed nothing.
        builder.HasIndex(e => new { e.Status, e.CreatedAt })
            .HasDatabaseName("IX_initial_wfp_export_jobs_status_created_at");

        // ⚠️ Decision 5 in the database: at most one Queued or Running job per fiscal year. Checking
        // "is there one already?" first is not enough on its own; two requests at the same moment
        // would both see none and both insert. This index makes the second insert fail, and the
        // service then returns the job that won.
        builder.HasIndex(e => e.FiscalYear)
            .IsUnique()
            .HasFilter($"[status] IN (N'{BackgroundJobStatus.Queued}', N'{BackgroundJobStatus.Running}')")
            .HasDatabaseName("IX_initial_wfp_export_jobs_fiscal_year_active");
    }
}
