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
    /// Warehouse-count movement per StockNo, for every StockNo with at least one
    /// stock_balances entry — unfiltered. Used by InventoryService's unscoped
    /// (Admin/SuperAdmin) view to surface StockNos recorded purely via warehouse stock input
    /// that never had any PR/delivery activity, and are therefore otherwise absent from
    /// IInventoryRepository.GetItemStockLevelsAsync entirely.
    ///
    /// Returns the two components separately rather than the single netted figure this
    /// method used to return (RAL-240): the Stock Overview grid has to attribute counted
    /// stock to its DELIVERED column and pool issues to DISTRIBUTED, and a pre-netted total
    /// cannot be decomposed back into the two. Callers wanting the old value take
    /// <see cref="StockNoPoolMovement.NetVariance"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, StockNoPoolMovement>> GetAllPoolMovementsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same decomposition as <see cref="GetAllPoolMovementsAsync"/>, restricted to the given
    /// stock numbers — the date-filtered Stock Overview view, whose item universe is already
    /// narrowed to items delivered in range. StockNos with no entries are simply absent from
    /// the result (caller treats missing as zero movement).
    /// </summary>
    Task<IReadOnlyDictionary<string, StockNoPoolMovement>> GetPoolMovementsByStockNosAsync(
        IReadOnlyCollection<string> stockNos, CancellationToken cancellationToken = default);

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

/// <summary>
/// Warehouse-count movement for one StockNo (RAL-240), kept as two separate components so
/// callers can report counted stock and pool issues independently.
/// </summary>
public sealed record StockNoPoolMovement(
    decimal GrossVariance,
    decimal Distributed)
{
    /// <summary>
    /// What the pool contributes to on-hand: counted quantity less what has been issued
    /// from it. This is the single figure GetAllVarianceTotalsAsync returned before RAL-240.
    /// </summary>
    public decimal NetVariance => GrossVariance - Distributed;

    /// <summary>
    /// The positive part of GrossVariance — stock the count brought in, reported under
    /// DELIVERED. A negative gross variance is shrinkage, not a receipt, so it is excluded
    /// here and carried by <see cref="Shrinkage"/> instead.
    /// </summary>
    public decimal Received => Math.Max(0m, GrossVariance);

    /// <summary>
    /// The negative part of GrossVariance (always &lt;= 0). Has no column of its own; it is
    /// added into on-hand so that Received + Shrinkage == GrossVariance and the on-hand
    /// total is unchanged from before RAL-240.
    /// </summary>
    public decimal Shrinkage => Math.Min(0m, GrossVariance);
}
