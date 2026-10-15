using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// What every background job has, whatever it produces (v1.9.0 — PPDO-228,
/// <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c> decisions 2 and 14, §5).
///
/// <para>
/// <b>One table per job kind, no table for this class.</b> It is mapped with EF Core's TPC strategy
/// (table-per-concrete-type): each kind, starting with <see cref="InitialWfpExportJob"/>, gets its own
/// table holding these columns plus its own. The claim, stuck-job recovery and expiry are written once
/// against this class (<c>IBackgroundJobRepository</c>) and work for every kind.
/// </para>
///
/// <para>
/// ⚠️ <b>Ids are unique across all job tables</b>, not just within one: they come from one shared
/// sequence, <c>background_job_ids</c>. TPC needs that, because EF identifies a <see cref="BackgroundJob"/>
/// by id alone.
/// </para>
/// </summary>
public abstract class BackgroundJob
{
    /// <summary>A Running job with no heartbeat for this long is presumed dead (spec decision 11).</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);

    /// <summary>A job claimed this many times without finishing is Failed instead of re-queued (decision 11).</summary>
    public const int MaxAttempts = 3;

    /// <summary>The message a job gets when it runs out of attempts.</summary>
    public const string StoppedUnexpectedlyMessage = "The export stopped unexpectedly. Run it again.";

    /// <summary>Primary key, from the shared <c>background_job_ids</c> sequence.</summary>
    public int Id { get; set; }

    public BackgroundJobStatus Status { get; set; } = BackgroundJobStatus.Queued;

    /// <summary>Who asked for it. The worker runs as this user (spec decision 8).</summary>
    public Guid RequestedById { get; set; }

    /// <summary>When it was asked for (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When a worker last claimed it (UTC). Null until the first claim.</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>The worker's last sign of life (UTC): set on claim, refreshed on every progress update.</summary>
    public DateTime? HeartbeatAt { get; set; }

    /// <summary>How many times it has been claimed. The claim increments it.</summary>
    public int Attempts { get; set; }

    /// <summary>Why it failed, shown to the user. At most 400 characters.</summary>
    public string? Message { get; set; }

    /// <summary>When it reached Done, DoneWithProblems or Failed (UTC).</summary>
    public DateTime? FinishedAt { get; set; }

    public User? RequestedBy { get; set; }
}
