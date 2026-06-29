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

    /// <summary>Returns the ceiling for (officeId, fiscalYear), or NotFound if unset.</summary>
    Task<ServiceResult<BudgetCeilingDto>> GetCeilingAsync(
        int officeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>Creates or updates the ceiling for (officeId, fiscalYear). Audit-logged.</summary>
    Task<ServiceResult<BudgetCeilingDto>> UpsertCeilingAsync(
        int officeId, int fiscalYear, decimal amount, CancellationToken ct = default);

    // ── Division Allocations ──────────────────────────────────────────────────

    /// <summary>
    /// Returns all division-allocation rows for the given office+FY.
    /// Divisions with no row are simply absent (caller shows ₱0 for them).
    /// </summary>
    Task<IReadOnlyList<DivisionAllocationDto>> GetAllocationsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Upserts the full set of division allocations for an office+FY.
    /// Returns BadRequest when: no ceiling exists, or Σ amounts exceeds the ceiling.
    /// Audit-logged per row.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<DivisionAllocationDto>>> UpsertAllocationsAsync(
        int officeId, int fiscalYear,
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
}
