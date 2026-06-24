namespace PPDO.Application.Services;

/// <summary>
/// Dev/maintenance utility — wipes all inventory-feature records so the v1.2 clean-slate
/// migration (RAL-97) can be applied (PurchaseRequests/Distributions gain a NOT NULL
/// division_id FK that rejects pre-existing rows).
/// </summary>
public interface IInventoryCleanupService
{
    /// <summary>
    /// Deletes all inventory records in FK-safe order:
    /// Distributions → DeliveryItems → Deliveries → PRItems → PurchaseRequests → ItemMasters.
    /// Returns the per-table deleted counts.
    /// </summary>
    Task<InventoryPurgeResult> PurgeAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Per-table counts of rows removed by <see cref="IInventoryCleanupService.PurgeAllAsync"/>.</summary>
public sealed record InventoryPurgeResult(
    int Distributions,
    int DeliveryItems,
    int Deliveries,
    int PRItems,
    int PurchaseRequests,
    int ItemMasters);
