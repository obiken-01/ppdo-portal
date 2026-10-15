using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <inheritdoc cref="IInitialWfpExportService" />
public sealed class InitialWfpExportService : IInitialWfpExportService
{
    public const string ForbiddenMessage   = "You do not have access to the bulk WFP export.";
    public const string NotFoundMessage    = "Export not found.";
    public const string NotReadyMessage    = "This export is not ready to download.";
    public const string ExpiredMessage     = "This export has expired. Run it again.";

    /// <summary>"Bulk export is for FY 2028 and later." (spec §3 case 12), from the break year, never a literal.</summary>
    public static string TooEarlyMessage
        => $"Bulk export is for FY {AipFiscalYears.FirstEnteredFiscalYear} and later.";

    /// <summary>"No office's FY 2028 AIP is marked as done yet." (spec §3 case 6).</summary>
    public static string NoneEligibleMessage(int fiscalYear)
        => $"No office's FY {fiscalYear} AIP is marked as done yet.";

    private readonly IInitialWfpExportJobRepository  _jobs;
    private readonly IWfpReportService               _report;
    private readonly IPermissionService              _permissions;
    private readonly IFileStore                      _files;
    private readonly TimeProvider                    _clock;
    private readonly ILogger<InitialWfpExportService> _logger;

    public InitialWfpExportService(
        IInitialWfpExportJobRepository jobs,
        IWfpReportService report,
        IPermissionService permissions,
        IFileStore files,
        TimeProvider clock,
        ILogger<InitialWfpExportService> logger)
    {
        _jobs        = jobs;
        _report      = report;
        _permissions = permissions;
        _files       = files;
        _clock       = clock;
        _logger      = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<InitialWfpExportRequestResult>> RequestAsync(
        int fiscalYear, User caller, CancellationToken ct = default)
    {
        if (!await IsPpdoReportReaderAsync(caller, ct))
            return ServiceResult<InitialWfpExportRequestResult>.Forbidden(ForbiddenMessage);

        if (!AipFiscalYears.IsEntered(fiscalYear))
            return ServiceResult<InitialWfpExportRequestResult>.BadRequest(TooEarlyMessage);

        // Decision 5: one active job per year. Whoever asks while it runs gets that job.
        InitialWfpExportJob? active = await _jobs.GetActiveByFiscalYearAsync(fiscalYear, ct);
        if (active is not null)
            return ServiceResult<InitialWfpExportRequestResult>.Ok(new(ToDto(active), Created: false));

        // Case 6: nothing to export yet, so no job. The worker re-lists the offices when it starts
        // (decision 9); this is only the "is there anything at all?" check.
        IReadOnlyList<WfpReportOfficeDto> eligible = await _report.GetInitialEligibleOfficesAsync(fiscalYear, caller, ct);
        if (eligible.Count == 0)
            return ServiceResult<InitialWfpExportRequestResult>.Conflict(NoneEligibleMessage(fiscalYear));

        InitialWfpExportJob job = new()
        {
            FiscalYear    = fiscalYear,
            Status        = BackgroundJobStatus.Queued,
            RequestedById = caller.Id,
            // CreatedAt is stamped by AppDbContext on save, like every entity's.
        };

        try
        {
            await _jobs.AddAsync(job, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Another request queued the year's job between our check and our insert. The database
            // index kept it to one job; answer with that one, as if we had seen it above.
            InitialWfpExportJob? winner = await _jobs.GetActiveByFiscalYearAsync(fiscalYear, ct);
            if (winner is null) throw;
            return ServiceResult<InitialWfpExportRequestResult>.Ok(new(ToDto(winner), Created: false));
        }

        _logger.LogInformation(
            "Initial WFP bulk export queued. JobId: {JobId}, FiscalYear: {FiscalYear}, UserId: {UserId}",
            job.Id, fiscalYear, caller.Id);

        job.RequestedBy = caller;
        return ServiceResult<InitialWfpExportRequestResult>.Ok(new(ToDto(job), Created: true));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<InitialWfpExportJobDto?>> GetLatestAsync(
        int fiscalYear, User caller, CancellationToken ct = default)
    {
        if (!await IsPpdoReportReaderAsync(caller, ct))
            return ServiceResult<InitialWfpExportJobDto?>.Forbidden(ForbiddenMessage);

        InitialWfpExportJob? latest = await _jobs.GetLatestByFiscalYearAsync(fiscalYear, ct);
        return ServiceResult<InitialWfpExportJobDto?>.Ok(latest is null ? null : ToDto(latest));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<InitialWfpExportJobDto>> GetAsync(int id, User caller, CancellationToken ct = default)
    {
        if (!await IsPpdoReportReaderAsync(caller, ct))
            return ServiceResult<InitialWfpExportJobDto>.Forbidden(ForbiddenMessage);

        InitialWfpExportJob? job = await _jobs.GetByIdAsync(id, ct);
        return job is null
            ? ServiceResult<InitialWfpExportJobDto>.NotFound(NotFoundMessage)
            : ServiceResult<InitialWfpExportJobDto>.Ok(ToDto(job));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<InitialWfpExportDownload>> OpenDownloadAsync(
        int id, User caller, CancellationToken ct = default)
    {
        if (!await IsPpdoReportReaderAsync(caller, ct))
            return ServiceResult<InitialWfpExportDownload>.Forbidden(ForbiddenMessage);

        InitialWfpExportJob? job = await _jobs.GetByIdAsync(id, ct);
        if (job is null)
            return ServiceResult<InitialWfpExportDownload>.NotFound(NotFoundMessage);

        // Past its expiry counts as expired even before the worker's cleanup has marked it (case 8).
        bool expired = job.Status == BackgroundJobStatus.Expired
                    || job.ExpiresAt is DateTime expiresAt && expiresAt <= _clock.GetUtcNow().UtcDateTime;
        if (expired)
            return ServiceResult<InitialWfpExportDownload>.Gone(ExpiredMessage);

        if (job.Status is not (BackgroundJobStatus.Done or BackgroundJobStatus.DoneWithProblems)
            || job.FileKey is null || job.FinishedAt is null)
            return ServiceResult<InitialWfpExportDownload>.Conflict(NotReadyMessage);

        // A finished job whose file has gone missing is, to the user, an expired one: run it again.
        Stream? content = await _files.OpenReadAsync(job.FileKey, ct);
        if (content is null)
        {
            _logger.LogWarning(
                "Initial WFP bulk export file missing. JobId: {JobId}, FileKey: {FileKey}", job.Id, job.FileKey);
            return ServiceResult<InitialWfpExportDownload>.Gone(ExpiredMessage);
        }

        return ServiceResult<InitialWfpExportDownload>.Ok(new(
            content, InitialWfpExportFiles.ZipFileName(job.FiscalYear, job.FinishedAt.Value)));
    }

    /// <summary>
    /// Decision 4: the readers <c>InitialWfpReadRule</c> lets read every office, which is <c>CanAccessWfp</c>
    /// (Permission_Matrix.md §3.4). Office users keep their per-office export.
    /// </summary>
    private async Task<bool> IsPpdoReportReaderAsync(User caller, CancellationToken ct)
    {
        if (await _permissions.CanAccessWfpAsync(caller, ct)) return true;

        _logger.LogWarning(
            "Permission denied. UserId: {UserId}, Feature: {Feature}", caller.Id, "InitialWfpBulkExport");
        return false;
    }

    private static InitialWfpExportJobDto ToDto(InitialWfpExportJob job) => new(
        job.Id,
        job.FiscalYear,
        job.Status.ToString(),
        job.DoneCount,
        job.TotalCount,
        InitialWfpExportFiles.ParseProblems(job.ProblemsJson),
        job.Message,
        job.RequestedBy?.FullName ?? string.Empty,
        job.CreatedAt,
        job.FinishedAt,
        job.ExpiresAt);
}
