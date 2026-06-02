using PPDO.Domain.Enums;

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
    /// Pass null for division to include all divisions (Admin/SuperAdmin view).
    /// </summary>
    Task<IReadOnlyList<ItemStockLevel>> GetItemStockLevelsAsync(
        Division? division,
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
