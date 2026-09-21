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

    /// <summary>
    /// Whether an office's own setup (division split, programme assignment) may still be changed
    /// for this fiscal year (v1.8.0 — PPDO-107, `Office_Setup_Spec.md` D10).
    ///
    /// True while the office holds at least one AIP group in an office-editable state (Draft,
    /// DepartmentReview, ReturnedByPpdo) — and true when the office has no AIP rows for the year
    /// at all, which is where setup normally happens, before any encoding.
    ///
    /// ⚠️ **Any group, not every group.** An office whose sectors are in different states still
    /// has work in its own hands, and freezing its division split because one sector was accepted
    /// would block the office from funding the ones still open.
    ///
    /// ⚠️ **Read this as "is the office still working", not as a permission.** The grant is checked
    /// separately; this is the state gate applied on top of it, and it is deliberately NOT applied
    /// to a PPDO caller holding <c>CanManagePpdoAllocation</c> — PPDO sets offices up across the
    /// whole cycle, including after acceptance.
    /// </summary>
    Task<bool> IsOfficeSetupEditableAsync(int officeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// The config office that owns an AIP office ref code, or null when none matches — longest
    /// match wins (v1.8.0 — PPDO-107 exposed it; the rule itself is <c>AipOfficeOwnership</c>).
    ///
    /// ⚠️ Needed because the programme-assignment payload carries a ref code and no office id, so
    /// an own-office caller cannot be checked against it without resolving the code first.
    /// </summary>
    Task<int?> ResolveOfficeIdForAipRefCodeAsync(string aipOfficeRefCode, CancellationToken ct = default);

    // ── Division Allocations ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the division-allocation rows for the given office+FY+fundingSourceId.
    /// Divisions with no row are simply absent (caller shows ₱0 for them).
    /// </summary>
    Task<IReadOnlyList<DivisionAllocationDto>> GetAllocationsAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default);

    /// <summary>
    /// Every fund's division-allocation rows for the given office+FY in one query (RAL-166
    /// follow-up) — for the Allocation page's per-fund-source panels, which previously fired
    /// one GetAllocationsAsync call per active fund in parallel. Shape mirrors
    /// <see cref="GetCeilingsAsync"/>: a flat list where each <see cref="DivisionAllocationDto"/>
    /// already carries its own FundingSourceId, so the caller groups by fund itself.
    /// </summary>
    Task<IReadOnlyList<DivisionAllocationDto>> GetAllocationsForAllFundsAsync(
        int officeId, int fiscalYear, CancellationToken ct = default);

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
