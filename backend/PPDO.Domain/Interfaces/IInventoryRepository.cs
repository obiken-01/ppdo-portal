namespace PPDO.Domain.Interfaces;

/// <summary>
/// Aggregate queries needed by the Inventory Dashboard — stock level computation
/// across PRItems, DeliveryItems, and Distributions.
/// These queries span multiple tables without navigating deep include chains,
/// using SQL-level joins and GROUP BY via LINQ query syntax.
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// Returns aggregated stock levels per StockNo across all PRs (or one division's PRs).
    /// Computes:
    ///   QtyOrdered     = SUM(PRItem.Quantity)          — total ordered
    ///   QtyDelivered   = SUM(DeliveryItem.QtyDelivered)— total received
    ///   QtyDistributed = SUM(Distribution.QtyIssued)   — total issued to divisions
    ///
    /// Only PRItems with a non-null StockNo are included.
    /// Pass null for divisionId to include all divisions (Admin/SuperAdmin view).
    /// </summary>
    Task<IReadOnlyList<ItemStockLevel>> GetItemStockLevelsAsync(
        int? divisionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct StockNos that had at least one delivery record with
    /// a DeliveryDate within [from, to] (inclusive), optionally scoped to a division.
    /// Used to power the "received in quarter" filter on the Stock Overview page.
    /// Stock level totals are computed separately — this only determines which
    /// items appear in the filtered result.
    /// </summary>
    Task<IReadOnlySet<string>> GetStockNosDeliveredInRangeAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int? divisionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated stock totals for a single StockNo — returned by
/// <see cref="IInventoryRepository.GetItemStockLevelsAsync"/>.
/// Application layer joins this with ItemMaster to build ledger rows and dashboard alerts.
/// </summary>
public sealed record ItemStockLevel(
    string StockNo,
    decimal QtyOrdered,
    decimal QtyDelivered,
    decimal QtyDistributed);
