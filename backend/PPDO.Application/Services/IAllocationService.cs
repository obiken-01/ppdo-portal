using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// Allocation service — budget ceiling, division allocations, PPA→division assignment,
/// and the WFP setup-gate query (v1.2 — RAL-99).
///
/// Amounts are always in PESOS (no ×1000 conversion — that lives in WFP page layer).
/// The Σ(allocations) ≤ ceiling rule is enforced in UpsertAllocationsAsync.
/// </summary>
public interface IAllocationService
{
    // ── Budget Ceiling ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the ceiling for (officeId, fiscalYear, fundingSourceId), or NotFound if unset.
    /// </summary>
    Task<ServiceResult<BudgetCeilingDto>> GetCeilingAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default);

    /// <summary>
    /// Returns every fund source's ceiling row for the given office+FY (v1.4.3 — RAL-154).
    /// A fund source with no ceiling set is simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<BudgetCeilingDto>> GetCeilingsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates the ceiling for (officeId, fiscalYear, fundingSourceId). Audit-logged.
    /// </summary>
    Task<ServiceResult<BudgetCeilingDto>> UpsertCeilingAsync(
        int officeId, int fiscalYear, int fundingSourceId, decimal amount, CancellationToken ct = default);

    // ── Division Allocations ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the division-allocation rows for the given office+FY+fundingSourceId.
    /// Divisions with no row are simply absent (caller shows ₱0 for them).
    /// </summary>
    Task<IReadOnlyList<DivisionAllocationDto>> GetAllocationsAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default);

    /// <summary>
    /// Upserts the full set of division allocations for an office+FY+fundingSourceId.
    /// Returns BadRequest when: no ceiling exists for that fund, or Σ amounts exceeds that
    /// fund's ceiling. Audit-logged per row.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<DivisionAllocationDto>>> UpsertAllocationsAsync(
        int officeId, int fiscalYear, int fundingSourceId,
        IReadOnlyList<UpsertDivisionAllocationDto> dtos,
        CancellationToken ct = default);

    // ── PPA → Division Assignments ────────────────────────────────────────────

    /// <summary>
    /// Returns all programs for the office+FY with their assigned division IDs.
    /// Programs with no assignments have DivisionIds = [].
    /// Returns an empty list when no non-archived AIP record exists for the FY.
    /// </summary>
    Task<IReadOnlyList<ProgramAssignmentDto>> GetProgramAssignmentsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Sets (replaces) the division assignments for one (officeRefCode, programRefCode) pair.
    /// An empty DivisionIds list clears all assignments.
    /// Audit-logged per add/remove. Returns NotFound when no division in the list exists.
    /// </summary>
    Task<ServiceResult<ProgramAssignmentDto>> UpsertProgramAssignmentAsync(
        UpsertProgramAssignmentDto dto, CancellationToken ct = default);

    // ── WFP Setup Gate ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the three-flag setup status for a given (office, FY, division).
    /// All three must be true before WFP expenditure entry is permitted.
    /// </summary>
    Task<AllocationSetupStatusDto> GetSetupStatusAsync(
        int officeId, int fiscalYear, int divisionId, CancellationToken ct = default);

    /// <summary>
    /// Office-level allocation-setup counts (fully set up / incomplete / not started)
    /// across all active offices for a fiscal year — used by the dashboard's
    /// "All Offices" view (RAL-60), where allocation can't be shown per-office.
    /// </summary>
    Task<AllocationSetupOverviewDto> GetSetupOverviewAsync(
        int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Resolves the General Fund <c>funding_sources.id</c> by Code "GF" (v1.4.3 — RAL-154).
    /// Shared by every caller that needs to treat a null/unselected fund source as General
    /// Fund, so the "GF" code string lives in exactly one place. Null if the GF row is
    /// somehow missing (should never happen post-migration).
    /// </summary>
    Task<int?> GetGeneralFundIdAsync(CancellationToken ct = default);
}
