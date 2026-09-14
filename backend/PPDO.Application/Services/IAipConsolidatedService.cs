using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The consolidated AIP — the province-wide record as PPDO's cross-office reviewers see it, one
/// Annex B sector sheet at a time (v1.8.0 Phase 4 — V18-55 / PPDO-73, <c>AIP_Review_Spec.md</c>
/// §6.3a, decisions 13 and 14).
///
/// <para>
/// ⚠️ <b>Not a new record.</b> It is the fiscal year's one base AIP record, read across every office
/// that has reached PPDO (decision 13). Nothing is assembled, copied or snapshotted.
/// </para>
///
/// <para>
/// ⚠️ <b>The gate is <c>CanReviewAllOffices</c>, not <c>IsHostOffice</c></b> — PPDO's own division
/// users are host-office users and must not see it (tracker B4). Checked here as well as at the
/// endpoint, as <c>AipReviewService</c> does.
/// </para>
/// </summary>
public interface IAipConsolidatedService
{
    /// <summary>
    /// One sector sheet for the fiscal year: rows of offices at <c>SubmittedToPpdo</c> or
    /// <c>Consolidated</c> only, with printed figures, plus submitted counts per sector and overall.
    /// An unopened year is an empty sheet; an unknown sector is a bad request.
    /// </summary>
    Task<ServiceResult<AipConsolidatedSheetDto>> GetSheetAsync(
        int fiscalYear, string? sector, User caller, CancellationToken ct = default);

    /// <summary>
    /// The whole fiscal year as the province's Annex B workbook (V18-60 / PPDO-84): all four sector
    /// sheets, the same offices and figures as the grid, the tree loaded once. FY≤2027 is a bad
    /// request, an unopened year not found.
    /// </summary>
    Task<ServiceResult<AipFormExportFileDto>> ExportWorkbookAsync(
        int fiscalYear, User caller, CancellationToken ct = default);
}
