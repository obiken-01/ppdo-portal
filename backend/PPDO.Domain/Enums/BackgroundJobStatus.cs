namespace PPDO.Domain.Enums;

/// <summary>
/// Where a <see cref="Entities.BackgroundJob"/> is in its life (v1.9.0 — PPDO-228,
/// <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c> §4). Stored as its name (nvarchar(20)).
///
/// <code>
/// Queued ──claim──▶ Running ──▶ Done | DoneWithProblems ──7 days──▶ Expired
///   ▲                  │
///   └──no heartbeat────┘   (after the 3rd attempt: Failed)
/// </code>
/// </summary>
public enum BackgroundJobStatus
{
    /// <summary>Waiting for a worker to claim it.</summary>
    Queued,

    /// <summary>Claimed by a worker, which refreshes the heartbeat as it goes.</summary>
    Running,

    /// <summary>Finished with nothing skipped.</summary>
    Done,

    /// <summary>Finished, but some parts were skipped and noted (spec decision 7).</summary>
    DoneWithProblems,

    /// <summary>The job itself failed; <c>message</c> says why.</summary>
    Failed,

    /// <summary>Its output was kept for the retention period and has been deleted.</summary>
    Expired,
}
