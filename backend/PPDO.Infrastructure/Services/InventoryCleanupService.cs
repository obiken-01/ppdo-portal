using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PPDO.Application.Services;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Wipes all inventory-feature records in FK-safe order (child → parent) so the v1.2
/// clean-slate migration can be applied. Uses raw SQL DELETE to bypass EF entity mapping —
/// safe even when the local DB is on the pre-migration schema (DivisionId column absent).
/// </summary>
public sealed class InventoryCleanupService : IInventoryCleanupService
{
    private readonly AppDbContext _db;
    private readonly ILogger<InventoryCleanupService> _logger;

    public InventoryCleanupService(AppDbContext db, ILogger<InventoryCleanupService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InventoryPurgeResult> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        // Raw SQL to bypass EF entity mapping — safe even on pre-migration schema.
        // Delete in FK-safe order: children first, parents last.
        int distributions    = await _db.Database.ExecuteSqlRawAsync("DELETE FROM [Distributions]",   cancellationToken);
        int deliveryItems    = await _db.Database.ExecuteSqlRawAsync("DELETE FROM [DeliveryItems]",   cancellationToken);
        int deliveries       = await _db.Database.ExecuteSqlRawAsync("DELETE FROM [Deliveries]",      cancellationToken);
        int prItems          = await _db.Database.ExecuteSqlRawAsync("DELETE FROM [PRItems]",         cancellationToken);
        int purchaseRequests = await _db.Database.ExecuteSqlRawAsync("DELETE FROM [PurchaseRequests]",cancellationToken);
        int itemMasters      = await _db.Database.ExecuteSqlRawAsync("DELETE FROM [ItemMasters]",     cancellationToken);

        _logger.LogWarning(
            "Inventory purged. Distributions: {Distributions}, DeliveryItems: {DeliveryItems}, " +
            "Deliveries: {Deliveries}, PRItems: {PRItems}, PurchaseRequests: {PurchaseRequests}, ItemMasters: {ItemMasters}",
            distributions, deliveryItems, deliveries, prItems, purchaseRequests, itemMasters);

        return new InventoryPurgeResult(
            distributions, deliveryItems, deliveries, prItems, purchaseRequests, itemMasters);
    }
}
