using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for the WFP division-allocation ledger (v1.4 WFP Rework — §8, RAL-122).
/// All query methods push WHERE filters to SQL — the full ledger table is never materialised
/// in memory just to find or sum one division+FY's rows.
/// </summary>
public interface IWfpAllocationLedgerRepository : IRepository<WfpDivisionAllocationLedger>
{
    /// <summary>Returns the ledger row for the unique (divisionId, fiscalYear, wfpRecordId) key, or null.</summary>
    Task<WfpDivisionAllocationLedger?> FindAsync(
        int divisionId, int fiscalYear, int wfpRecordId, CancellationToken ct = default);

    /// <summary>
    /// Sum of UsedAmount across every ledger row for (divisionId, fiscalYear), optionally
    /// excluding one wfp_record_id's row (used when checking "would this save exceed the
    /// division's remaining allocation" — the record being saved is excluded so its OLD
    /// contribution isn't double-counted against its new, would-be contribution).
    /// </summary>
    Task<decimal> SumUsedAmountAsync(
        int divisionId, int fiscalYear, int? excludeWfpRecordId, CancellationToken ct = default);
}
