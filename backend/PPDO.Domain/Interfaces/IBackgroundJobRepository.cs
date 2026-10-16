using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// The queue operations every job kind shares, written once against <see cref="BackgroundJob"/>
/// (v1.9.0 — PPDO-228, <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c> decisions 10–12 and 14).
/// Each method takes the job kind as <typeparamref name="TJob"/> and works on that kind's table only.
///
/// <para>
/// Kind-specific reads and writes (create, latest for a year, progress counts) live on the kind's own
/// repository, e.g. <see cref="IInitialWfpExportJobRepository"/>.
/// </para>
/// </summary>
public interface IBackgroundJobRepository
{
    /// <summary>
    /// Takes the oldest Queued job of this kind and marks it Running: <c>claimed_at</c> and
    /// <c>heartbeat_at</c> set to <paramref name="now"/>, <c>attempts</c> + 1. Returns it, or null when
    /// nothing is queued.
    ///
    /// <para>
    /// ⚠️ <b>Atomic.</b> Two workers calling this at the same moment never get the same job: one wins, the
    /// other gets the next job or null. This is the one method that is written in the database's own
    /// SQL, so it is the one to rewrite if the database changes.
    /// </para>
    /// </summary>
    Task<TJob?> ClaimNextAsync<TJob>(DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob;

    /// <summary>
    /// Running jobs of this kind with no heartbeat for <see cref="BackgroundJob.StuckAfter"/> (decision 11):
    /// back to Queued, or Failed with <see cref="BackgroundJob.StoppedUnexpectedlyMessage"/> once they have
    /// had <see cref="BackgroundJob.MaxAttempts"/> attempts. Returns what was done to each, for the log.
    /// </summary>
    Task<IReadOnlyList<RecoveredJob>> RecoverStuckAsync<TJob>(DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob;

    /// <summary>
    /// Finished jobs of this kind whose output is past its <see cref="IExpiringJob.ExpiresAt"/>. The
    /// caller deletes each file, then calls <see cref="MarkExpiredAsync{TJob}"/> (decision 12).
    /// </summary>
    Task<IReadOnlyList<TJob>> GetExpiredAsync<TJob>(DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob, IExpiringJob;

    /// <summary>Marks a finished job Expired. Its row is kept: job rows are the history.</summary>
    Task MarkExpiredAsync<TJob>(int id, CancellationToken ct = default)
        where TJob : BackgroundJob;

    /// <summary>
    /// Refreshes a Running job's <c>heartbeat_at</c>, so it is not taken for stuck. Returns false when
    /// the job is no longer Running (recovered by another worker meanwhile), and the caller should stop.
    /// </summary>
    Task<bool> HeartbeatAsync<TJob>(int id, DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob;

    /// <summary>
    /// Ends a Running job as Failed with <paramref name="message"/> (cut to 400 characters) and
    /// <c>finished_at</c> = <paramref name="now"/> (v1.9.0 — PPDO-229). Returns false, changing nothing,
    /// when the job is no longer Running.
    /// </summary>
    Task<bool> FailAsync<TJob>(int id, string message, DateTime now, CancellationToken ct = default)
        where TJob : BackgroundJob;
}

/// <summary>What <see cref="IBackgroundJobRepository.RecoverStuckAsync{TJob}"/> did to one stuck job.</summary>
/// <param name="Id">The job.</param>
/// <param name="Attempts">How many times it had been claimed.</param>
/// <param name="Failed">True when it ran out of attempts and was Failed; false when it was re-queued.</param>
public sealed record RecoveredJob(int Id, int Attempts, bool Failed);
