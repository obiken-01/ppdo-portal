namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>One division's allocated amount in one fund (v1.4.5 — RAL-161).</summary>
public record DivisionFundAmountDto(
    int    FundingSourceId,
    string FundCode,
    string FundName,
    decimal Amount);

/// <summary>
/// One division's WFP status + activity coverage + allocation, scoped to the Dashboard's
/// single office (PPDO) and fiscal year. WfpStatus = "Draft" | "Final" | "Not started".
/// </summary>
public record DivisionWfpStatusDto(
    int    DivisionId,
    string? DivisionCode,
    string DivisionName,
    string WfpStatus,
    int    ActivitiesWithExpenditures,
    int    TotalActivities,
    decimal TotalAllocated,
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
/// The PPDO-scoped Budget Planning Dashboard (v1.4.5 — RAL-161). Replaces the old multi-office
/// <see cref="PlanningDashboardDto"/>: Budget Planning is permanently scoped to PPDO in practice,
/// so this carries PPDO's own LDIP/AIP counts plus a per-division WFP + per-fund ceiling/allocation
/// breakdown, instead of a fleet-wide "N offices set up" summary.
///
/// For a caller without CanManageAllocation (division-scoped Staff), the service clamps
/// <see cref="WfpByDivision"/> and every <see cref="FundCeilingDto.ByDivision"/> entry to just
/// the caller's own division — never trust a client-supplied divisionId for this (RAL-136 pattern).
/// </summary>
public record PpdoDashboardDto(
    int    FiscalYear,
    IReadOnlyList<int> AvailableFiscalYears,
    int    OfficeId,
    string OfficeCode,
    string OfficeName,
    OfficeLdipSummaryDto Ldip,
    OfficeAipSummaryDto  Aip,
    IReadOnlyList<DivisionWfpStatusDto> WfpByDivision,
    IReadOnlyList<FundCeilingDto>       CeilingByFund
);
