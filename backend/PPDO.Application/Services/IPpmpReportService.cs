using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

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
}
