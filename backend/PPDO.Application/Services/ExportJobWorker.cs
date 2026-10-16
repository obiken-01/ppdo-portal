using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// The bulk initial WFP export worker (v1.9.0 — PPDO-229, <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c>
/// decisions 7–17). Each <see cref="RunOnceAsync"/> is one pass over <c>initial_wfp_export_jobs</c>:
///
/// <list type="number">
///   <item><description><b>Clean up</b>: delete ZIPs past their 7 days, mark their jobs Expired (decision 12).</description></item>
///   <item><description><b>Recover</b>: a Running job with no heartbeat for 10 minutes goes back to Queued, or Failed after 3 attempts (decision 11).</description></item>
///   <item><description><b>Claim</b> the oldest Queued job, and stop if there is none.</description></item>
///   <item><description><b>Run</b> it: build each eligible office's workbook as the requester, into one ZIP with a README; save it; finish Done, DoneWithProblems or Failed; ring the requester's bell.</description></item>
/// </list>
///
/// <para>
/// One job per pass: the timer comes back in 30 seconds for the next. ⚠️ Nothing here knows about
/// Azure: the files go through <see cref="IFileStore"/> and the host is whatever calls this
/// (<c>ExportJobTimerFunctions</c> today).
/// </para>
/// </summary>
public sealed class ExportJobWorker : IExportJobWorker
{
    /// <summary>Spec decision 8: the requester can no longer open the report for every office.</summary>
    public const string NoAccessMessage = "You no longer have access to this report.";

    /// <summary>Anything else that stops the job itself, such as storage being down. The detail is in the log.</summary>
    public const string JobFailedMessage = "The export failed. Run it again.";

    /// <summary>What the README and the job say about an office whose workbook threw. The detail is in the log.</summary>
    public const string OfficeFailedReason = "The report could not be built for this office.";

    /// <summary>The README's encoding. The byte-order mark makes older Notepad read an office name's ñ correctly.</summary>
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>The job kind, as it appears in every log line's <c>JobKind</c>.</summary>
    private const string JobKind = nameof(InitialWfpExportJob);

    private readonly IBackgroundJobRepository       _queue;
    private readonly IInitialWfpExportJobRepository _exports;
    private readonly IUserRepository                _users;
    private readonly IPermissionService             _permissions;
    private readonly IWfpReportService              _report;
    private readonly IWfpReportExcelService         _excel;
    private readonly IFileStore                     _files;
    private readonly INotificationService           _notifications;
    private readonly TimeProvider                   _clock;
    private readonly ILogger<ExportJobWorker>       _logger;

    public ExportJobWorker(
        IBackgroundJobRepository queue,
        IInitialWfpExportJobRepository exports,
        IUserRepository users,
        IPermissionService permissions,
        IWfpReportService report,
        IWfpReportExcelService excel,
        IFileStore files,
        INotificationService notifications,
        TimeProvider clock,
        ILogger<ExportJobWorker> logger)
    {
        _queue         = queue;
        _exports       = exports;
        _users         = users;
        _permissions   = permissions;
        _report        = report;
        _excel         = excel;
        _files         = files;
        _notifications = notifications;
        _clock         = clock;
        _logger        = logger;
    }

    /// <inheritdoc />
    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        int expired   = await CleanUpExpiredAsync(ct);
        int recovered = await RecoverStuckAsync(ct);
        InitialWfpExportJob? job = await _queue.ClaimNextAsync<InitialWfpExportJob>(Now(), ct);

        // Decision 15: one Debug line per pass, so the log shows the timer is alive.
        _logger.LogDebug(
            "Export worker pass. Expired: {ExpiredCount}, Recovered: {RecoveredCount}, Claimed: {ClaimedCount}",
            expired, recovered, job is null ? 0 : 1);

        if (job is not null)
            await RunJobAsync(job, ct);
    }

    // ── 1. Clean up (decision 12) ───────────────────────────────────────────────

    private async Task<int> CleanUpExpiredAsync(CancellationToken ct)
    {
        IReadOnlyList<InitialWfpExportJob> expired = await _queue.GetExpiredAsync<InitialWfpExportJob>(Now(), ct);

        foreach (InitialWfpExportJob job in expired)
        {
            using IDisposable? scope = BeginJobScope(job.Id);
            try
            {
                // File first, then the row: if the delete fails, the job stays Done and the next pass
                // tries again, rather than an Expired job leaving its file behind for good.
                if (job.FileKey is not null)
                    await _files.DeleteAsync(job.FileKey, ct);
                await _queue.MarkExpiredAsync<InitialWfpExportJob>(job.Id, ct);

                _logger.LogInformation("Export expired; file deleted. FileKey: {FileKey}", job.FileKey);
            }
            catch (Exception ex) when (!IsShutdown(ex, ct))
            {
                _logger.LogWarning(ex, "Expired export could not be cleaned up; the next pass will try again.");
            }
        }
        return expired.Count;
    }

    // ── 2. Recover stuck jobs (decision 11) ─────────────────────────────────────

    private async Task<int> RecoverStuckAsync(CancellationToken ct)
    {
        // The repository decides re-queue or fail (the 3-attempt cap); this only reports what it did.
        IReadOnlyList<RecoveredJob> recovered = await _queue.RecoverStuckAsync<InitialWfpExportJob>(Now(), ct);

        foreach (RecoveredJob job in recovered)
        {
            using IDisposable? scope = BeginJobScope(job.Id);
            if (job.Failed)
                _logger.LogError(
                    "Export job ran out of attempts after stopping unexpectedly; marked Failed. Attempt: {Attempt}",
                    job.Attempts);
            else
                _logger.LogWarning(
                    "Export job recovered from stuck; queued again. Attempt: {Attempt}", job.Attempts);
        }
        return recovered.Count;
    }

    // ── 3–4. Run one claimed job ────────────────────────────────────────────────

    /// <summary>
    /// Runs a job this worker has just claimed. Every line logged in here, including by the report and
    /// Excel services underneath, carries the job's <c>JobId</c> and <c>JobKind</c> (decision 15).
    /// </summary>
    private async Task RunJobAsync(InitialWfpExportJob job, CancellationToken ct)
    {
        using IDisposable? scope = BeginJobScope(job.Id);
        long started = _clock.GetTimestamp();
        _logger.LogInformation(
            "Export job claimed. Attempt: {Attempt}, FiscalYear: {FiscalYear}", job.Attempts, job.FiscalYear);

        try
        {
            await BuildAndFinishAsync(job, started, ct);
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            // The job itself failed (storage down, the database gone mid-run), not one office.
            await FailAsync(job, JobFailedMessage, started, ex, ct);
        }
        // A shutdown is let out on purpose: the job stays Running, and stuck recovery re-queues it.
    }

    private async Task BuildAndFinishAsync(InitialWfpExportJob job, long started, CancellationToken ct)
    {
        // Decision 8: the worker runs as the requester, so it reads exactly what they could.
        User? requester = await _users.GetByIdWithDivisionAsync(job.RequestedById, ct);
        if (requester is null || !requester.IsActive || !await _permissions.CanAccessWfpAsync(requester, ct))
        {
            await FailAsync(job, NoAccessMessage, started, exception: null, ct);
            return;
        }

        // Decision 9: whoever is Consolidated now, not when the job was asked for.
        IReadOnlyList<WfpReportOfficeDto> offices =
            await _report.GetInitialEligibleOfficesAsync(job.FiscalYear, requester, ct);
        if (offices.Count == 0)
        {
            await FailAsync(job, InitialWfpExportService.NoneEligibleMessage(job.FiscalYear), started, exception: null, ct);
            return;
        }

        // The total goes up first, so the page can show "0 of 12" before the first office is done.
        if (!await _exports.UpdateProgressAsync(job.Id, 0, offices.Count, Now(), ct))
        {
            LogTakenAway();
            return;
        }

        List<WfpReportOfficeDto> included = [];
        List<SkippedOffice>      skipped  = [];
        DateTime builtAt;

        // Decision 17: the whole ZIP is built in memory and saved once at the end, so a job that
        // fails leaves nothing half-written in the store.
        using MemoryStream zipBytes = new();
        using (ZipArchive zip = new(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < offices.Count; i++)
            {
                WfpReportOfficeDto office = offices[i];

                (byte[]? workbook, string? skipReason) = await TryBuildOfficeAsync(job, office, requester, ct);
                if (workbook is not null)
                {
                    WriteEntry(zip, InitialWfpExportFiles.OfficeFileName(office.OfficeCode, job.FiscalYear), workbook);
                    included.Add(office);
                }
                else
                {
                    skipped.Add(new SkippedOffice(office, skipReason!));
                }

                // The heartbeat: progress after every office, built or skipped, refreshes heartbeat_at,
                // so a job that is still moving is never taken for stuck. False means another worker
                // has re-queued it meanwhile; that run owns the job now, so this one stops.
                if (!await _exports.UpdateProgressAsync(job.Id, i + 1, offices.Count, Now(), ct))
                {
                    LogTakenAway();
                    return;
                }
            }

            builtAt = Now();
            string readme = InitialWfpExportFiles.Readme(
                job.FiscalYear, builtAt, requester.FullName, included, skipped);
            WriteEntry(zip, InitialWfpExportFiles.ReadmeFileName, [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(readme)]);
        }

        // ── 5. Save once, then finish ──
        string fileKey = InitialWfpExportFiles.FileKey(job.Id, job.FiscalYear, builtAt);
        zipBytes.Position = 0;
        await _files.SaveAsync(fileKey, zipBytes, ct);

        BackgroundJobStatus status = skipped.Count == 0 ? BackgroundJobStatus.Done : BackgroundJobStatus.DoneWithProblems;
        List<InitialWfpExportProblemDto> problems = skipped
            .Select(s => new InitialWfpExportProblemDto(s.Office.OfficeCode, s.Reason))
            .ToList();

        InitialWfpExportCompletion completion = new(
            status,
            DoneCount: offices.Count,
            TotalCount: offices.Count,
            InitialWfpExportFiles.SerializeProblems(problems),
            fileKey,
            ExpiresAt: builtAt + InitialWfpExportJob.KeepFor);

        // finished_at = builtAt: the download names the ZIP from finished_at, so it matches the key.
        if (!await _exports.CompleteAsync(job.Id, completion, builtAt, ct))
        {
            // Re-queued while saving: the next run makes its own file, so ours is an orphan.
            await _files.DeleteAsync(fileKey, ct);
            LogTakenAway();
            return;
        }

        _logger.LogInformation(
            "Export job finished. Status: {Status}, Done: {DoneCount}, Total: {TotalCount}, Skipped: {SkippedCount}, ElapsedMs: {ElapsedMs}",
            status, offices.Count, offices.Count, skipped.Count, ElapsedMs(started));

        await _notifications.NotifyAsync(
            NotificationTypes.InitialWfpExportReady,
            requester,
            NotificationText.InitialWfpExportReady(job.FiscalYear, included.Count, offices.Count),
            NotificationLinks.WfpReport(job.FiscalYear),
            ct);
    }

    /// <summary>
    /// One office's workbook, exactly as its own <b>Export</b> button makes it (decision 1), or the
    /// reason it was skipped.
    /// </summary>
    private async Task<(byte[]? Workbook, string? SkipReason)> TryBuildOfficeAsync(
        InitialWfpExportJob job, WfpReportOfficeDto office, User requester, CancellationToken ct)
    {
        long started = _clock.GetTimestamp();

        // ⚠️ The per-office catch (decision 7): whatever goes wrong with one office is noted and
        // skipped, and the others are still built. Only a shutdown gets past it.
        try
        {
            ServiceResult<WfpReportDto> report = await _report.GetInitialReportAsync(
                office.OfficeId, job.FiscalYear, requestedDivisionId: null, requester, ct);

            // E.g. returned for edits after the list was read: the report says why, in its own words.
            if (!report.IsSuccess)
            {
                string reason = report.Error ?? OfficeFailedReason;
                _logger.LogWarning(
                    "Office skipped. OfficeCode: {OfficeCode}, Reason: {Reason}", office.OfficeCode, reason);
                return (null, reason);
            }

            byte[] workbook = _excel.Export(report.Value!);

            _logger.LogInformation(
                "Office built. OfficeCode: {OfficeCode}, ElapsedMs: {ElapsedMs}", office.OfficeCode, ElapsedMs(started));
            return (workbook, null);
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            // The README gets the plain reason; the exception goes to the log, under this job's id.
            _logger.LogWarning(ex,
                "Office skipped. OfficeCode: {OfficeCode}, Reason: {Reason}", office.OfficeCode, OfficeFailedReason);
            return (null, OfficeFailedReason);
        }
    }

    /// <summary>Ends the job Failed with a message the user sees. An unexpected cause is logged with its exception.</summary>
    private async Task FailAsync(
        InitialWfpExportJob job, string message, long started, Exception? exception, CancellationToken ct)
    {
        _logger.LogError(exception,
            "Export job failed. Reason: {Reason}, ElapsedMs: {ElapsedMs}", message, ElapsedMs(started));

        try
        {
            if (!await _queue.FailAsync<InitialWfpExportJob>(job.Id, message, Now(), ct))
                LogTakenAway();
        }
        catch (Exception ex) when (!IsShutdown(ex, ct))
        {
            // Could not even record the failure (the database is down too). The job stays Running,
            // so stuck recovery retries it in 10 minutes or fails it after the 3rd attempt.
            _logger.LogError(ex, "Export job could not be marked Failed; stuck recovery will handle it.");
        }
    }

    // ── Small helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// The job's log scope. A dictionary rather than <c>new { JobId, JobKind }</c>: Application Insights
    /// and Seq turn a list of key/value pairs into named properties (<c>customDimensions.JobId</c>), but
    /// keep an anonymous object only as one string, which cannot be searched by id.
    /// </summary>
    private IDisposable? BeginJobScope(int jobId)
        => _logger.BeginScope(new Dictionary<string, object> { ["JobId"] = jobId, ["JobKind"] = JobKind });

    private void LogTakenAway()
        => _logger.LogWarning("Export job is no longer Running here (re-queued as stuck); this run stops.");

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    /// <summary>
    /// True when <paramref name="ex"/> is the host stopping us. That is the one exception the catches
    /// let through: it is not the office's fault or the job's, and the job is recovered later.
    /// </summary>
    private static bool IsShutdown(Exception ex, CancellationToken ct)
        => ex is OperationCanceledException && ct.IsCancellationRequested;

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;

    private long ElapsedMs(long started) => (long)_clock.GetElapsedTime(started).TotalMilliseconds;
}
