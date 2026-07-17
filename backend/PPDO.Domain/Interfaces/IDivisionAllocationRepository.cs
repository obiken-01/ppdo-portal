using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="DivisionAllocation"/> with scoped reads (RAL-163 — perf
/// audit Tier 1). All query methods push WHERE filters to SQL — the full table is never
/// materialised in memory just to find one office's allocations or check one division's.
/// </summary>
public interface IDivisionAllocationRepository : IRepository<DivisionAllocation>
{
    /// <summary>
    /// Returns allocation rows for any of the given division ids, scoped to one fiscal
    /// year+funding source. Used for both reading an office's allocations (divisionIds =
    /// that office's divisions) and resolving which of a batch of upserts already exist.
    /// </summary>
    Task<IReadOnlyList<DivisionAllocation>> GetByDivisionIdsAsync(
        IReadOnlyList<int> divisionIds, int fiscalYear, int fundingSourceId, CancellationToken ct = default);

    /// <summary>True if a positive-amount allocation row exists for (divisionId, fiscalYear, fundingSourceId).</summary>
    Task<bool> HasPositiveAllocationAsync(
        int divisionId, int fiscalYear, int fundingSourceId, CancellationToken ct = default);

    /// <summary>Returns every allocation row for one fiscal year+funding source, across all divisions.</summary>
    Task<IReadOnlyList<DivisionAllocation>> GetByFiscalYearAndFundingSourceAsync(
        int fiscalYear, int fundingSourceId, CancellationToken ct = default);
}
