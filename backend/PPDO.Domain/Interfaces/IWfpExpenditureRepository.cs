using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side WFP expenditure reads (v1.4 WFP Rework —
/// RAL-120). Mirrors <see cref="IWfpRepository"/>'s pattern: all query methods push WHERE
/// filters to SQL so the full wfp_expenditures/wfp_expenditure_periods/wfp_procurement_items
/// tables are never loaded in memory just to read one expenditure's children.
/// </summary>
public interface IWfpExpenditureRepository : IRepository<WfpExpenditure>
{
    /// <summary>
    /// Returns the expenditure whose integer PK equals <paramref name="id"/>, or null.
    /// Needed because the base <see cref="IRepository{T}.GetByIdAsync"/> uses a Guid key.
    /// </summary>
    Task<WfpExpenditure?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>WfpExpenditurePeriod rows WHERE expenditure_id = <paramref name="expenditureId"/>, ordered by period_no.</summary>
    Task<IReadOnlyList<WfpExpenditurePeriod>> GetPeriodsByExpenditureIdAsync(int expenditureId, CancellationToken ct = default);

    /// <summary>WfpProcurementItem rows WHERE expenditure_id = <paramref name="expenditureId"/>, ordered by period_no.</summary>
    Task<IReadOnlyList<WfpProcurementItem>> GetProcurementItemsByExpenditureIdAsync(int expenditureId, CancellationToken ct = default);
}
