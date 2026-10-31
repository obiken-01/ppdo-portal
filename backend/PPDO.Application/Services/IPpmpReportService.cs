using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// PPMP Report preview (v1.5 — RAL-183) — read-only, prelude to the `.xlsx` export (RAL-184).
/// Re-projects the same AIP hierarchy + WFP procurement-item data the WFP report uses, but
/// item-grained and procurement-only, into the province's own PPMP form layout
/// (<c>ppmp admin2027.xlsx</c>; spec in <c>docs/v1.5/PPMP_Report_Findings.md</c> §8). Never
/// writes; never recomputes the WFP quarter roll-up — it reuses the same period→quarter rules as
/// <c>WfpExpenditureCalculator</c>, applied at the item grain.
///
/// The office picker is shared with the WFP report (<see cref="IWfpReportService.GetEligibleOfficesAsync"/>) —
/// "offices with a WFP for the fiscal year" is identical for both reports, so this interface adds
/// no offices method of its own.
/// </summary>
public interface IPpmpReportService
{
    /// <summary>
    /// Assembles the PPMP report for one office + fiscal year: fund source → program → project →
    /// activity → account → item rows (with per-quarter qty/amount and account/hierarchy totals).
    /// When <paramref name="divisionId"/> is null, merges procurement items across every division
    /// of the office (office-consolidated view — allocation officers / SuperAdmin); when provided,
    /// scopes to that one division only (RAL-136 — division-scoped users never see another
    /// division's figures).
    /// </summary>
    Task<ServiceResult<PpmpReportDto>> GetReportAsync(
        int officeId, int fiscalYear, int? divisionId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The initial PPMP for an entered year (v1.9.2 — PPDO-263, <c>docs/v1.9/Initial_PPMP_Report_Spec.md</c>):
    /// the same item-grained form, fed from the consolidated AIP's procurement items instead of WFP
    /// items. <see cref="PpmpReportDto.Source"/> is <c>"Aip"</c>.
    /// <list type="bullet">
    ///   <item><description>403 without <c>CanAccessWfp</c> (decision 8): PPDO only, never a guest office's head.</description></item>
    ///   <item><description>404 for a missing office or AIP; 409 until every AIP group of the office is Consolidated (decision 5).</description></item>
    ///   <item><description>Division: PPDO's own office follows <see cref="WfpDivisionScope"/> (RAL-136); another office honours the requested division, by the activity's tag (decision 11).</description></item>
    /// </list>
    /// </summary>
    Task<ServiceResult<PpmpReportDto>> GetInitialReportAsync(
        int officeId, int fiscalYear, int? requestedDivisionId, User caller, CancellationToken cancellationToken = default);
}
