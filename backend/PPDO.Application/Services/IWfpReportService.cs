using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// WFP Report preview (RAL-132) — read-only, prelude to the full report generator. Never
/// writes anything; assembles existing AIP hierarchy + WFP expenditure data (across all of an
/// office's divisions, or one division only — RAL-136) into the WFP FINAL sheet's layout. All
/// totals are read straight from what <see cref="WfpExpenditureService"/> already computed on
/// save — this service never recomputes Q1–Q4/Net/Total itself.
/// </summary>
public interface IWfpReportService
{
    /// <summary>
    /// Offices with at least a Draft WFP for <paramref name="fiscalYear"/> — the Report page's
    /// office picker. Filters <see cref="IBudgetPlanningDashboardService.GetDashboardAsync"/>'s
    /// WfpByOffice rather than running a new query.
    /// </summary>
    Task<IReadOnlyList<WfpReportOfficeDto>> GetEligibleOfficesAsync(
        int fiscalYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assembles the full report for one office + fiscal year: function band → program →
    /// project → activity → expense-class subsections (with sub-totals) → activity grand
    /// total. When <paramref name="divisionId"/> is null, merges WFP expenditures across
    /// every division of the office (finance-officer consolidated view); when provided,
    /// scopes the report to that one division only (RAL-136 — division-scoped users must
    /// never see another division's figures).
    /// </summary>
    Task<ServiceResult<WfpReportDto>> GetReportAsync(
        int officeId, int fiscalYear, int? divisionId = null, CancellationToken cancellationToken = default);
}
