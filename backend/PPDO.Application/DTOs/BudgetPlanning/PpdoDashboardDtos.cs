namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One division's allocated amount in one fund, plus how much of it this division has actually
/// used in WFP so far and how much is left (v1.4.5 — RAL-161; Used/Remaining added RAL-176).
/// Remaining = Amount − Used — the SAME division-scoped ledger calculation the WFP Entry Wizard
/// uses (<c>WfpCeilingService.GetStatusAsync</c>), not the office-wide unallocated-ceiling figure
/// on <see cref="FundCeilingDto.Remaining"/> (that one is intentionally identical across every
/// division and answers a different question — see its own doc comment).
/// </summary>
public record DivisionFundAmountDto(
    int    FundingSourceId,
    string FundCode,
    string FundName,
    decimal Amount,
    decimal Used,
    decimal Remaining);

/// <summary>
/// One division's AIP progress and money, scoped to the Dashboard's host office and fiscal year
/// (PPDO-20 — replaces <c>DivisionWfpStatusDto</c>).
///
/// ⚠️ <b>This is not the old DTO with a field added.</b> Three of its predecessor's members were
/// WFP concepts — <c>WfpStatus</c>, <c>ActivitiesWithExpenditures</c> (activities carrying a WFP
/// *expenditure*), and a total that meant the same. Dashboard decisions 3 and 4
/// (<c>docs/v1.8/Budget_Planning_Dashboard_Requirements.md</c>) retire all three from this page:
/// WFP is about to be redesigned into an update of what AIP creation already produced, so
/// reporting on its present shape would teach a model that goes wrong within a release. Money and
/// coverage now come from the AIP. Keeping both counts side by side would have left two "coverage"
/// numbers on one row meaning different things — exactly the confusion being removed.
///
/// <see cref="DivisionCode"/> stays nullable: <c>Allocation_Requirements.md</c> §5 makes the code
/// optional with the name as the fallback identifier. Render the name, not an empty pill.
/// </summary>
public record DivisionSummaryDto(
    int    DivisionId,
    string? DivisionCode,
    string DivisionName,
    decimal Allocated,
    decimal CostedInAip,
    decimal Remaining,
    int    CostedActivityCount,
    int    TotalActivities,
    string AipStatus,
    string SubmissionStatus,
    IReadOnlyList<DivisionFundAmountDto> AllocationByFund);

/// <summary>One division's share of a fund's office-wide ceiling.</summary>
public record FundDivisionShareDto(
    int    DivisionId,
    string? DivisionCode,
    string DivisionName,
    decimal Amount);

/// <summary>
/// One funding source's office-wide ceiling for the fiscal year, its per-division allocation
/// breakdown, and the unallocated remainder — the Dashboard's per-fund pie-chart data.
/// Ceiling/Remaining are office-level figures (NOT per division — a division's Amount inside
/// ByDivision is its own allocated share of this same ceiling).
/// </summary>
public record FundCeilingDto(
    int    FundingSourceId,
    string FundCode,
    string FundName,
    decimal Ceiling,
    decimal Remaining,
    IReadOnlyList<FundDivisionShareDto> ByDivision);

/// <summary>
/// Just the fiscal-year picker data (RAL-166 follow-up) — the resolved fiscal year plus every
/// year with an AIP. Split out of <see cref="PpdoDashboardDto"/> so callers that only need the
/// picker (e.g. the Report page) don't pay for the LDIP/AIP/WFP-by-division/ceiling-by-fund
/// build on every page load.
/// </summary>
public record FiscalYearsDto(
    int FiscalYear,
    IReadOnlyList<int> AvailableFiscalYears);

/// <summary>
/// The PPDO-scoped Budget Planning Dashboard (v1.4.5 — RAL-161). Replaces the old multi-office
/// <see cref="PlanningDashboardDto"/>: Budget Planning is permanently scoped to PPDO in practice,
/// so this carries PPDO's own LDIP/AIP counts plus a per-division WFP + per-fund ceiling/allocation
/// breakdown, instead of a fleet-wide "N offices set up" summary.
///
/// For a caller without CanManagePpdoAllocation (division-scoped Staff), the service clamps
/// <see cref="ByDivision"/> and every <see cref="FundCeilingDto.ByDivision"/> entry to just
/// the caller's own division — never trust a client-supplied divisionId for this (RAL-136 pattern).
/// That clamp is the mechanism behind "money and tables clamped server-side" in the PPDO-20 spec;
/// it survived the WFP → AIP rename of this list and is pinned by test.
/// </summary>
public record PpdoDashboardDto(
    int    FiscalYear,
    IReadOnlyList<int> AvailableFiscalYears,
    int    OfficeId,
    string OfficeCode,
    string OfficeName,
    OfficeLdipSummaryDto Ldip,
    OfficeAipSummaryDto  Aip,
    IReadOnlyList<DivisionSummaryDto> ByDivision,
    IReadOnlyList<FundCeilingDto>     CeilingByFund
);

/// <summary>
/// One office's row on the cross-office dashboard table (PPDO-20 —
/// <c>GET /api/budget-planning/dashboard/offices</c>). Read-only: nothing here is written back,
/// and the endpoint that serves it resolves scope through <c>OfficeScope.ResolveForReview</c> or
/// <c>ResolveForCeiling</c>, never <c>Resolve</c>.
///
/// Slim by construction — no free-text AIP columns. A fat AIP DTO once produced a 1.2 MB response
/// (<c>docs/PERFORMANCE_GUIDELINES.md</c>), and this list is rendered as a grid that shows none of
/// those fields.
/// </summary>
/// <param name="CeilingAmount">Total published ceiling across every fund. <b>Null means no ceiling
/// has been published at all</b> — distinct from a published ceiling of zero, which is a decision
/// somebody made. The UI reads the difference (stage 1 risk vs. a real figure), so do not
/// coalesce it.</param>
/// <param name="CostedInAip">Sum of the office's AIP activity totals. Zero when it has no AIP.</param>
/// <param name="IsOverCeiling">The office has costed more in its AIP than its published ceiling
/// allows. False whenever no ceiling is published — there is nothing to be over.</param>
/// <param name="SubmissionStatus">Constant <c>"Todo"</c> until Phase 4 adds a submission entity
/// (spec §7). Rendered from a constant on purpose, so the layout does not move when it becomes
/// real.</param>
/// <param name="ReviewerName">The office's budget-planning reviewer. <b>Null means nobody in that
/// office can submit</b> — the row's "Cannot submit / None — assign" state, not merely a blank.</param>
public record OfficeSummaryDto(
    int      OfficeId,
    string   OfficeCode,
    string   OfficeName,
    bool     IsHostOffice,
    decimal? CeilingAmount,
    decimal  CostedInAip,
    int      ActivityCount,
    string   AipStatus,
    string   SubmissionStatus,
    bool     IsOverCeiling,
    string?  ReviewerName
);
