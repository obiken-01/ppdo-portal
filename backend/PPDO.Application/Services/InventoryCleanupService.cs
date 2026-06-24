using Microsoft.Extensions.Logging;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Wipes all inventory-feature records in FK-safe order (child → parent) so the v1.2
/// clean-slate migration can be applied. Each table is deleted and saved in its own pass
/// to keep ordering deterministic regardless of cascade configuration.
/// </summary>
public sealed class InventoryCleanupService : IInventoryCleanupService
{
    private readonly IRepository<Distribution>    _distributions;
    private readonly IRepository<DeliveryItem>    _deliveryItems;
    private readonly IRepository<Delivery>        _deliveries;
    private readonly IRepository<PRItem>          _prItems;
    private readonly IRepository<PurchaseRequest> _purchaseRequests;
    private readonly IRepository<ItemMaster>      _itemMasters;
    private readonly ILogger<InventoryCleanupService> _logger;

    public InventoryCleanupService(
        IRepository<Distribution>    distributions,
        IRepository<DeliveryItem>    deliveryItems,
        IRepository<Delivery>        deliveries,
        IRepository<PRItem>          prItems,
        IRepository<PurchaseRequest> purchaseRequests,
        IRepository<ItemMaster>      itemMasters,
        ILogger<InventoryCleanupService> logger)
    {
        _distributions    = distributions;
        _deliveryItems    = deliveryItems;
        _deliveries       = deliveries;
        _prItems          = prItems;
        _purchaseRequests = purchaseRequests;
        _itemMasters      = itemMasters;
        _logger           = logger;
    }

    /// <inheritdoc />
    public async Task<InventoryPurgeResult> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        int distributions    = await PurgeAsync(_distributions, cancellationToken);
        int deliveryItems    = await PurgeAsync(_deliveryItems, cancellationToken);
        int deliveries       = await PurgeAsync(_deliveries, cancellationToken);
        int prItems          = await PurgeAsync(_prItems, cancellationToken);
        int purchaseRequests = await PurgeAsync(_purchaseRequests, cancellationToken);
        int itemMasters      = await PurgeAsync(_itemMasters, cancellationToken);

        _logger.LogWarning(
            "Inventory purged. Distributions: {Distributions}, DeliveryItems: {DeliveryItems}, " +
            "Deliveries: {Deliveries}, PRItems: {PRItems}, PurchaseRequests: {PurchaseRequests}, ItemMasters: {ItemMasters}",
            distributions, deliveryItems, deliveries, prItems, purchaseRequests, itemMasters);

        return new InventoryPurgeResult(
            distributions, deliveryItems, deliveries, prItems, purchaseRequests, itemMasters);
    }

    private static async Task<int> PurgeAsync<T>(IRepository<T> repo, CancellationToken ct) where T : class
    {
        IReadOnlyList<T> all = await repo.GetAllAsync(ct);
        foreach (T entity in all)
            await repo.DeleteAsync(entity, ct);
        if (all.Count > 0)
            await repo.SaveChangesAsync(ct);
        return all.Count;
    }
}
