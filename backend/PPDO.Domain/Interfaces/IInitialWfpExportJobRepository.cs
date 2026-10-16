using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// The bulk initial WFP export's own reads and writes on <c>initial_wfp_export_jobs</c>
/// (v1.9.0 — PPDO-228). The queue operations it shares with every job kind are on
/// <see cref="IBackgroundJobRepository"/>.
/// </summary>
public interface IInitialWfpExportJobRepository
{
    /// <summary>
    /// Inserts and saves a new job.
    /// ⚠️ Throws <see cref="Common.UniqueConstraintViolationException"/> when the year already has a
    /// Queued or Running job (decision 5); the caller then reads that job with
    /// <see cref="GetActiveByFiscalYearAsync"/>.
    /// </summary>
    Task AddAsync(InitialWfpExportJob job, CancellationToken ct = default);

    /// <summary>One job by id, with its requester, untracked. Null when there is none.</summary>
    Task<InitialWfpExportJob?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>The year's most recent job in any status, with its requester, untracked. Null when there is none.</summary>
    Task<InitialWfpExportJob?> GetLatestByFiscalYearAsync(int fiscalYear, CancellationToken ct = default);

    /// <summary>The year's Queued or Running job, with its requester, untracked. Null when there is none.</summary>
    Task<InitialWfpExportJob?> GetActiveByFiscalYearAsync(int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Records progress on a Running job: the counts, and a fresh heartbeat. Returns false when the job
    /// is no longer Running.
    /// </summary>
    Task<bool> UpdateProgressAsync(int id, int doneCount, int totalCount, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Ends a Running job with its ZIP saved (v1.9.0 — PPDO-229): Done or DoneWithProblems, the final
    /// counts and problems, the file, its expiry, and <c>finished_at</c> = <paramref name="now"/>.
    /// Returns false, changing nothing, when the job is no longer Running.
    /// </summary>
    Task<bool> CompleteAsync(int id, InitialWfpExportCompletion completion, DateTime now, CancellationToken ct = default);
}

/// <summary>What <see cref="IInitialWfpExportJobRepository.CompleteAsync"/> writes on a finished export.</summary>
/// <param name="Status">Done, or DoneWithProblems when any office was skipped.</param>
/// <param name="DoneCount">Offices processed, built or skipped.</param>
/// <param name="TotalCount">Offices that were eligible when the job ran.</param>
/// <param name="ProblemsJson">The skipped offices, <c>[{ officeCode, reason }]</c>, or null.</param>
/// <param name="FileKey">Where the ZIP was saved in the file store.</param>
/// <param name="ExpiresAt">When the ZIP is deleted.</param>
public sealed record InitialWfpExportCompletion(
    BackgroundJobStatus Status,
    int DoneCount,
    int TotalCount,
    string? ProblemsJson,
    string FileKey,
    DateTime ExpiresAt);
