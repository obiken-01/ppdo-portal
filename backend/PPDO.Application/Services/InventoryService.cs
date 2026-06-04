using Microsoft.Extensions.Logging;
using PPDO.Application.DTOs.Inventory;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Inventory Dashboard stats and Item Ledger computation.
///
/// Division scope:
///   Staff/Observer → pass their division to IInventoryRepository and PR queries.
///   Admin/SuperAdmin → pass null (all divisions).
///
/// Stock level formula:
///   OnHand        = QtyDelivered - QtyDistributed
///   IsLowStock    = OnHand ≤ ReorderQty (and OnHand > 0)
///   IsOutOfStock  = OnHand ≤ 0
///
/// PR status grouping for stat cards:
///   "FullyDeliveredOrCompleted" = PRStatus.FullyDelivered OR PRStatus.Completed
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IInventoryRepository        _inventory;
    private readonly IPurchaseRequestRepository  _prs;
    private readonly IItemMasterRepository       _items;
    private readonly ILogger<InventoryService>   _logger;

    public InventoryService(
        IInventoryRepository inventory,
        IPurchaseRequestRepository prs,
        IItemMasterRepository items,
        ILogger<InventoryService> logger)
    {
        _inventory = inventory;
        _prs       = prs;
        _items     = items;
        _logger    = logger;
    }

    // ── GetStatsAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InventoryStatsDto> GetStatsAsync(
        User requester,
        CancellationToken cancellationToken = default)
    {
        Division? division = ScopeFor(requester);

        // Load PRs for division-scoped counts and total value.
        IReadOnlyList<PurchaseRequest> prs = division.HasValue
            ? await _prs.GetByDivisionAsync(division.Value, cancellationToken)
            : await _prs.GetAllAsync(cancellationToken);

        // Group 1 — Purchase Request stat cards.
        PRStatsGroupDto prGroup = new(
            Total:                   prs.Count,
            Open:                    prs.Count(p => p.Status == PRStatus.Open),
            PartiallyDelivered:      prs.Count(p => p.Status == PRStatus.PartiallyDelivered),
            FullyDeliveredOrCompleted: prs.Count(p =>
                p.Status is PRStatus.FullyDelivered or PRStatus.Completed));

        // Group 2 — Inventory Alert stat cards.
        // Stock levels from the aggregate repository query.
        IReadOnlyList<ItemStockLevel> stockLevels =
            await _inventory.GetItemStockLevelsAsync(division, cancellationToken);

        IReadOnlyList<ItemMaster> catalog = await _items.GetAllAsync(cancellationToken);
        Dictionary<string, ItemMaster> catalogMap =
            catalog.ToDictionary(i => i.StockNo, i => i);

        int inStock        = 0;
        int lowOrOutStock  = 0;

        foreach (ItemStockLevel level in stockLevels)
        {
            decimal onHand = level.QtyDelivered - level.QtyDistributed;
            int reorderQty = catalogMap.TryGetValue(level.StockNo, out ItemMaster? master)
                ? master.ReorderQty : 0;

            if (onHand > reorderQty)
                inStock++;
            else
                lowOrOutStock++;
        }

        decimal totalPRValue = prs.Sum(p => p.TotalAmount);

        // UniqueItemsTracked — count distinct StockNos with any PR activity (within scope).
        int uniqueItems = stockLevels.Count;

        AlertsGroupDto alertsGroup = new(
            InStock:            inStock,
            LowOrOutOfStock:    lowOrOutStock,
            TotalPRValue:       totalPRValue,
            UniqueItemsTracked: uniqueItems);

        return new InventoryStatsDto(prGroup, alertsGroup);
    }

    // ── GetItemLedgerAsync ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemLedgerRowDto>> GetItemLedgerAsync(
        User requester,
        DateOnly? deliveryDateFrom = null,
        DateOnly? deliveryDateTo   = null,
        CancellationToken cancellationToken = default)
    {
        Division? division = ScopeFor(requester);

        IReadOnlyList<ItemStockLevel> stockLevels =
            await _inventory.GetItemStockLevelsAsync(division, cancellationToken);

        // If a delivery date range is specified, restrict to items that had at
        // least one delivery within that window. Totals remain all-time figures.
        if (deliveryDateFrom.HasValue && deliveryDateTo.HasValue)
        {
            IReadOnlySet<string> deliveredStockNos =
                await _inventory.GetStockNosDeliveredInRangeAsync(
                    dateFrom: deliveryDateFrom.Value,
                    dateTo:   deliveryDateTo.Value,
                    division: division,
                    cancellationToken: cancellationToken);

            stockLevels = stockLevels
                .Where(l => deliveredStockNos.Contains(l.StockNo))
                .ToList();
        }

        IReadOnlyList<ItemMaster> catalog = await _items.GetAllAsync(cancellationToken);
        Dictionary<string, ItemMaster> catalogMap =
            catalog.ToDictionary(i => i.StockNo, i => i);

        List<ItemLedgerRowDto> rows = new(stockLevels.Count);

        foreach (ItemStockLevel level in stockLevels)
        {
            decimal onHand = level.QtyDelivered - level.QtyDistributed;

            catalogMap.TryGetValue(level.StockNo, out ItemMaster? master);
            string itemName  = master?.Description ?? level.StockNo;
            int    reorderQty = master?.ReorderQty ?? 0;

            if (onHand <= 0)
            {
                _logger.LogWarning(
                    "Low stock alert — out of stock. StockNo: {StockNo}, ItemName: {ItemName}, RemainingQty: {RemainingQty}",
                    level.StockNo, itemName, onHand);
            }
            else if (onHand <= reorderQty)
            {
                _logger.LogWarning(
                    "Low stock alert. StockNo: {StockNo}, ItemName: {ItemName}, RemainingQty: {RemainingQty}",
                    level.StockNo, itemName, onHand);
            }

            rows.Add(new ItemLedgerRowDto(
                StockNo:         level.StockNo,
                Description:     itemName,
                Category:        master?.Category,
                Unit:            master?.Unit ?? string.Empty,
                UnitCost:        master?.UnitCost ?? 0m,
                ItemType:        master?.ItemType,
                ReorderQty:      reorderQty,
                QtyOrdered:      level.QtyOrdered,
                QtyDelivered:    level.QtyDelivered,
                QtyDistributed:  level.QtyDistributed,
                OnHand:          onHand,
                IsLowStock:      onHand > 0 && onHand <= reorderQty,
                IsOutOfStock:    onHand <= 0));
        }

        return rows.OrderBy(r => r.StockNo).ToList();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the division to scope queries to, or null for Admin/SuperAdmin (all divisions).
    /// </summary>
    private static Division? ScopeFor(User requester)
        => requester.Role is UserRole.Staff or UserRole.Observer
            ? requester.Division
            : null;
}
