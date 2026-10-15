using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The bulk initial WFP export's requests (v1.9.0 — PPDO-228, <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c>
/// §3–§4). This only queues and reads jobs; the worker (<see cref="IExportJobWorker"/>, PPDO-229) builds them.
///
/// <para>
/// ⚠️ <b>PPDO report readers only</b> (decision 4): every method answers 403 to a caller without
/// <c>CanAccessWfp</c>, the users <c>InitialWfpReadRule</c> lets read every office. Jobs are per fiscal
/// year and shared by those readers (spec §3 case 11): any of them may read or download any job.
/// </para>
/// </summary>
public interface IInitialWfpExportService
{
    /// <summary>
    /// Queues an export for the year, or returns the year's Queued or Running one (decision 5).
    /// 400 for FY 2027 and earlier; 409 when no office is Consolidated yet (no job is created).
    /// </summary>
    Task<ServiceResult<InitialWfpExportRequestResult>> RequestAsync(
        int fiscalYear, User caller, CancellationToken ct = default);

    /// <summary>The year's most recent job in any status, or a null value when it has none (the endpoint's 204).</summary>
    Task<ServiceResult<InitialWfpExportJobDto?>> GetLatestAsync(
        int fiscalYear, User caller, CancellationToken ct = default);

    /// <summary>One job; 404 when there is none.</summary>
    Task<ServiceResult<InitialWfpExportJobDto>> GetAsync(int id, User caller, CancellationToken ct = default);

    /// <summary>
    /// A finished job's ZIP, opened for streaming. 404 when there is no such job, 409 while it is not
    /// finished (or failed), 410 once it has expired.
    /// </summary>
    Task<ServiceResult<InitialWfpExportDownload>> OpenDownloadAsync(
        int id, User caller, CancellationToken ct = default);
}
