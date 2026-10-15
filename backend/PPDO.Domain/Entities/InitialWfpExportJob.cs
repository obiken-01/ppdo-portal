namespace PPDO.Domain.Entities;

/// <summary>
/// One request to export every Consolidated office's initial WFP for a fiscal year as one ZIP
/// (v1.9.0 — PPDO-228, <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c> §5). Table
/// <c>initial_wfp_export_jobs</c>: the <see cref="BackgroundJob"/> columns plus these.
///
/// <para>
/// ⚠️ <b>At most one Queued or Running job per fiscal year</b> (decision 5). A filtered unique index
/// holds that in the database, so two PPDO users pressing <i>Download all</i> at the same moment
/// still end up sharing one job.
/// </para>
/// </summary>
public sealed class InitialWfpExportJob : BackgroundJob, IExpiringJob
{
    /// <summary>A finished ZIP is kept this long, then deleted (decision 6).</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    public int FiscalYear { get; set; }

    /// <summary>Offices processed so far, built or skipped.</summary>
    public int DoneCount { get; set; }

    /// <summary>Offices to process, known once the worker has listed them (decision 9). 0 until then.</summary>
    public int TotalCount { get; set; }

    /// <summary>The skipped offices as JSON, <c>[{ officeCode, reason }]</c>, or null when none were.</summary>
    public string? ProblemsJson { get; set; }

    /// <inheritdoc />
    public string? FileKey { get; set; }

    /// <inheritdoc />
    public DateTime? ExpiresAt { get; set; }
}
