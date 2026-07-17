using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="BudgetCeiling"/> with scoped reads (RAL-163 — perf audit
/// Tier 1). All query methods push WHERE filters to SQL — the full table is never materialised
/// in memory just to find one office+FY+fund's ceiling or one fiscal year's ceilings.
/// </summary>
public interface IBudgetCeilingRepository : IRepository<BudgetCeiling>
{
    /// <summary>Returns the ceiling row for the unique (officeId, fiscalYear, fundingSourceId) key, or null.</summary>
    Task<BudgetCeiling?> FindAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default);

    /// <summary>Returns every ceiling row (one per funding source) for one office+fiscal year.</summary>
    Task<IReadOnlyList<BudgetCeiling>> GetByOfficeAndFiscalYearAsync(
        int officeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>Returns every ceiling row for one fiscal year+funding source, across all offices.</summary>
    Task<IReadOnlyList<BudgetCeiling>> GetByFiscalYearAndFundingSourceAsync(
        int fiscalYear, int fundingSourceId, CancellationToken ct = default);
}
