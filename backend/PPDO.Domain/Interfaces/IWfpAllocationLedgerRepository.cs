using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for the WFP division-allocation ledger (v1.4 WFP Rework — §8, RAL-122).
/// All query methods push WHERE filters to SQL — the full ledger table is never materialised
/// in memory just to find or sum one division+FY's rows.
/// </summary>
public interface IWfpAllocationLedgerRepository : IRepository<WfpDivisionAllocationLedger>
{
    /// <summary>
    /// Returns the ledger row for the unique (divisionId, fiscalYear, fundingSourceId,
    /// wfpRecordId) key, or null (v1.4.3 — RAL-154 widened this key to include the fund).
    /// </summary>
    Task<WfpDivisionAllocationLedger?> FindAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int wfpRecordId, CancellationToken ct = default);

    /// <summary>
    /// Sum of UsedAmount across every ledger row for (divisionId, fiscalYear, fundingSourceId),
    /// optionally excluding one wfp_record_id's row (used when checking "would this save exceed
    /// the division's remaining allocation for this fund" — the record being saved is excluded
    /// so its OLD contribution isn't double-counted against its new, would-be contribution).
    /// </summary>
    Task<decimal> SumUsedAmountAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int? excludeWfpRecordId, CancellationToken ct = default);

    /// <summary>
    /// Distinct FundingSourceId values currently tracked in the ledger for one WFP record
    /// (v1.4.3 — RAL-154). Used by <c>WfpCeilingService.UpsertLedgerForActivityAsync</c> so a
    /// fund that's no longer used by any of the record's expenditures still gets its row
    /// recomputed down to zero, rather than left stale at its last positive amount.
    /// </summary>
    Task<IReadOnlyList<int>> GetFundingSourceIdsForRecordAsync(int wfpRecordId, CancellationToken ct = default);
}
