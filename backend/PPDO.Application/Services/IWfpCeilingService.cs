using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// Ceiling monitoring for WFP expenditure entry (v1.4 WFP Rework — §8, RAL-122). Two
/// independent checks, both computed server-side:
///   1. AIP budget (per activity): Σ WFP expenditure totals for the activity, across ALL
///      divisions of the office AND ALL funding sources, vs. the AIP activity's Total × 1000
///      (v1.4.3 §2 D3 — stays aggregate; AIP data has no per-fund breakdown to split against).
///   2. Division allocation: read from <c>wfp_division_allocation_ledger</c> (never a live
///      SUM across wfp_expenditures) — Remaining = DivisionAllocation.Amount − Σ(used_amount
///      across that division+FY+funding-source's ledger rows). Fund-scoped since v1.4.3 —
///      RAL-154: each expenditure is checked against its own funding source's allocation, with
///      a null funding source falling back to General Fund.
///
/// <see cref="ValidateExpenditureSaveAsync"/> is called from RAL-120's
/// <c>WfpExpenditureService.SaveExpenditureAsync</c> before any write — every save is
/// blocked (not just Finalize) if it would push either ceiling over its limit.
/// <see cref="ValidateRecordForFinalizeAsync"/> is <c>WfpService.FinalizeAsync</c>'s backstop
/// (should be unreachable in practice once every save is blocked).
/// </summary>
public interface IWfpCeilingService
{
    /// <summary>
    /// Read-only ceiling status for a given AIP activity + division + fiscal year — what
    /// ticket #9's context header displays and live-validates against while typing.
    /// </summary>
    Task<WfpCeilingStatusDto> GetStatusAsync(
        int aipActivityId, int divisionId, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Checks whether saving <paramref name="wfpActivityId"/>'s expenditure with a would-be
    /// total of <paramref name="newExpenditureTotal"/> would exceed either ceiling.
    /// <paramref name="fundingSourceId"/> is the expenditure's own funding source (v1.4.3 —
    /// RAL-154) — null when unselected, in which case the division-allocation check falls back
    /// to General Fund. The AIP-budget check (step 1) stays aggregate regardless (§2 D3).
    /// <paramref name="excludeExpenditureId"/> is the expenditure's own Id when updating (so
    /// its OLD total isn't double-counted against its new, would-be total).
    /// Returns null when both ceilings are satisfied, or an error message naming which
    /// ceiling and by how much otherwise.
    /// </summary>
    Task<string?> ValidateExpenditureSaveAsync(
        int wfpActivityId, decimal newExpenditureTotal, int? fundingSourceId,
        int? excludeExpenditureId, CancellationToken ct = default);

    /// <summary>
    /// Upserts the <c>wfp_division_allocation_ledger</c> row for the WFP record that owns
    /// <paramref name="wfpActivityId"/>, recomputing UsedAmount from the current sum of all
    /// that record's expenditure totals. No-ops when the record has no division (nothing to
    /// track). Called after every successful expenditure save.
    /// </summary>
    Task UpsertLedgerForActivityAsync(int wfpActivityId, CancellationToken ct = default);

    /// <summary>
    /// Backstop check across every AIP activity + the division allocation for an entire WFP
    /// record — called by <c>WfpService.FinalizeAsync</c>. Returns null when both ceilings are
    /// satisfied for every activity/the division, or an error message otherwise.
    /// </summary>
    Task<string?> ValidateRecordForFinalizeAsync(int wfpRecordId, CancellationToken ct = default);
}
