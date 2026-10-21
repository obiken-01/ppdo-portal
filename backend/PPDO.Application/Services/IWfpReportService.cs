using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

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

    // ── Initial WFP from the consolidated AIP (v1.9.0 — PPDO-224, Initial_WFP_Report_Spec.md) ──
    //
    // For an entered year (AipFiscalYears.IsEntered) there are no WFP records: the report is read
    // off the AIP on every request. The two methods above serve FY <= 2027 only and are unchanged.

    /// <summary>
    /// The offices whose AIP groups for <paramref name="fiscalYear"/> are <b>all</b> Consolidated
    /// (decision 5), status "Consolidated". A PPDO reader (<c>CanAccessWfp</c>) gets every such office;
    /// anyone else at most their own (decision 6).
    /// </summary>
    Task<IReadOnlyList<WfpReportOfficeDto>> GetInitialEligibleOfficesAsync(
        int fiscalYear, User caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// The initial WFP for one office, built from its consolidated AIP, with
    /// <see cref="WfpReportDto.Source"/> = <c>"Aip"</c>.
    /// <list type="bullet">
    ///   <item><description><b>404</b> for an office the caller may not read, the same answer as a missing one.</description></item>
    ///   <item><description><b>409</b> while any of the office's AIP groups is not Consolidated.</description></item>
    ///   <item><description>Division: the caller's own office follows the WFP report's rule (RAL-136); another office honours <paramref name="requestedDivisionId"/>.</description></item>
    /// </list>
    /// </summary>
    Task<ServiceResult<WfpReportDto>> GetInitialReportAsync(
        int officeId, int fiscalYear, int? requestedDivisionId, User caller, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller may read any office's initial WFP (PPDO-240, <c>InitialWfpReadRule.MayReadAny</c>):
    /// a PPDO reader, or an office's department head. False for a guest-office encoder, whom the
    /// endpoints answer 403 before looking any office up.
    /// </summary>
    Task<bool> MayReadInitialReportsAsync(User caller, CancellationToken cancellationToken = default);
}
