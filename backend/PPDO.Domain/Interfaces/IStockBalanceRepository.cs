using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Stock-balance (warehouse physical-count ledger) data access contract (RAL-193).
/// Extends <see cref="IRepository{T}"/> with the queries needed by StockBalanceService
/// and InventoryService's on-hand formula.
/// </summary>
public interface IStockBalanceRepository : IRepository<StockBalance>
{
    /// <summary>
    /// Returns every entry for a StockNo, newest first — the per-item history /
    /// reconciliation view.
    /// </summary>
    Task<IReadOnlyList<StockBalance>> GetByStockNoAsync(
        string stockNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns SUM(VarianceQty) grouped by StockNo, for exactly the given stock numbers —
    /// never the full ledger. Used by InventoryService to fold the running physical-count
    /// adjustment into the Admin/SuperAdmin unscoped on-hand formula. StockNos with no
    /// entries are simply absent from the result (caller treats missing as 0).
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetTotalVarianceByStockNosAsync(
        IReadOnlyCollection<string> stockNos, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns SUM(VarianceQty) grouped by StockNo for every StockNo that has at least one
    /// stock_balances entry — unfiltered. Used by InventoryService's unscoped (Admin/
    /// SuperAdmin) view to surface StockNos recorded purely via warehouse stock input that
    /// never had any PR/delivery activity, and are therefore otherwise absent from
    /// IInventoryRepository.GetItemStockLevelsAsync entirely.
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetAllVarianceTotalsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the entry matching a StockNo + EffectiveDate pair exactly — the bulk-import
    /// upsert key (re-uploading the same StockNo + date overwrites that entry).
    /// </summary>
    Task<StockBalance?> FindByStockNoAndEffectiveDateAsync(
        string stockNo, DateOnly effectiveDate, CancellationToken cancellationToken = default);
}
