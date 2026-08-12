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
    /// Returns SUM(VarianceQty) minus any Distribution.QtyIssued already issued against this
    /// StockNo's warehouse-count pool (RAL-223 — see <see cref="Domain.Entities.Distribution.StockBalanceId"/>),
    /// grouped by StockNo, for exactly the given stock numbers — never the full ledger. Used
    /// by InventoryService to fold the *remaining* physical-count-sourced on-hand into the
    /// Admin/SuperAdmin unscoped on-hand formula, and by StockBalanceService to compute the
    /// system-on-hand snapshot for a new/edited entry. StockNos with no entries are simply
    /// absent from the result (caller treats missing as 0).
    /// </summary>
    Task<IReadOnlyDictionary<string, decimal>> GetTotalVarianceByStockNosAsync(
        IReadOnlyCollection<string> stockNos, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same net-of-distributions calculation as <see cref="GetTotalVarianceByStockNosAsync"/>,
    /// grouped by StockNo for every StockNo that has at least one stock_balances entry —
    /// unfiltered. Used by InventoryService's unscoped (Admin/SuperAdmin) view to surface
    /// StockNos recorded purely via warehouse stock input that never had any PR/delivery
    /// activity, and are therefore otherwise absent from
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

    /// <summary>
    /// Returns the warehouse-count "pool" for one StockNo (RAL-223) — the most recent entry's
    /// Id/EffectiveDate (used as this pool's provenance/FIFO sort key when Distribution picks
    /// from it), the gross SUM(VarianceQty), and the total already distributed against it.
    /// Null when the StockNo has no stock_balances entries at all. Consumed by
    /// DistributionService so warehouse-count stock can be issued the same way delivery
    /// batches are.
    /// </summary>
    Task<WarehouseCountPoolRow?> GetPoolByStockNoAsync(
        string stockNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every Distribution issued against this StockNo's warehouse-count pool (i.e.
    /// <see cref="Domain.Entities.Distribution.StockBalanceId"/> set, joined to a stock_balances
    /// row with this StockNo), oldest DateIssued first. Display-only — used by
    /// DistributionService.GetItemSummaryAsync to list existing issuances under the synthetic
    /// "Warehouse Count" stock source row.
    /// </summary>
    Task<IReadOnlyList<DistributionBreakdownRow>> GetDistributionsByStockNoAsync(
        string stockNo, CancellationToken cancellationToken = default);
}

/// <summary>
/// The warehouse-count pool for one StockNo (RAL-223) — see
/// <see cref="IStockBalanceRepository.GetPoolByStockNoAsync"/>.
/// </summary>
public sealed record WarehouseCountPoolRow(
    Guid     LatestStockBalanceId,
    DateOnly EffectiveDate,
    decimal  GrossVariance,
    decimal  TotalDistributed)
{
    /// <summary>GrossVariance minus what's already been distributed against this pool.</summary>
    public decimal Remaining => GrossVariance - TotalDistributed;
}
