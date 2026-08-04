using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Inventory;
using PPDO.Domain.Entities;
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
/// Admin/SuperAdmin's unscoped view (division == null) additionally folds in the RAL-193
/// warehouse physical-count ledger: OnHand = SUM(StockBalance.VarianceQty) + QtyDelivered -
/// QtyDistributed. Staff/Observer's division-scoped view is unchanged — a PPDO-wide count
/// can't be correctly attributed to one division's subset of movements (see
/// StockBalanceService's class doc for the full formula rationale). A StockNo counted purely
/// via warehouse stock input, with no PR/delivery activity at all, would otherwise never
/// appear here (GetItemStockLevelsAsync's universe is PRItems rows) — MergeCountOnlyStockLevels
/// adds it back in as a zero-movement row so its counted quantity still surfaces.
///
/// PR status grouping for stat cards:
///   "FullyDeliveredOrCompleted" = PRStatus.FullyDelivered OR PRStatus.Completed
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IInventoryRepository        _inventory;
    private readonly IPurchaseRequestRepository  _prs;
    private readonly IItemMasterRepository       _items;
    private readonly IStockBalanceRepository     _stockBalances;
    private readonly ILogger<InventoryService>   _logger;

    public InventoryService(
        IInventoryRepository inventory,
        IPurchaseRequestRepository prs,
        IItemMasterRepository items,
        IStockBalanceRepository stockBalances,
        ILogger<InventoryService> logger)
    {
        _inventory     = inventory;
        _prs           = prs;
        _items         = items;
        _stockBalances = stockBalances;
        _logger        = logger;
    }

    // ── GetStatsAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<InventoryStatsDto> GetStatsAsync(
        User requester,
        CancellationToken cancellationToken = default)
    {
        DivisionScope scope = DivisionScope.Resolve(requester);

        // Office users (Staff with no division) have no inventory scope —
        // return empty stats rather than leaking every division's data.
        if (scope.SeeNothing)
            return EmptyStats();

        int? division = scope.DivisionId;

        // Group 1 — Purchase Request stat cards + total value, computed as SQL aggregates
        // (Count/Sum) scoped to the division — never loads PR rows into memory.
        PurchaseRequestStatsAggregate prStats =
            await _prs.GetStatsAggregateAsync(division, cancellationToken);

        PRStatsGroupDto prGroup = new(
            Total:                     prStats.Total,
            Open:                      prStats.Open,
            PartiallyDelivered:        prStats.PartiallyDelivered,
            FullyDeliveredOrCompleted: prStats.FullyDeliveredOrCompleted);

        // Group 2 — Inventory Alert stat cards.
        // Stock levels from the aggregate repository query.
        IReadOnlyList<ItemStockLevel> stockLevels =
            await _inventory.GetItemStockLevelsAsync(division, cancellationToken);

        // RAL-193: physical-count variance only applies to the unscoped (Admin/SuperAdmin)
        // view — see class doc. Empty for Staff/Observer, so onHand below is unchanged. Also
        // folds in StockNos recorded purely via warehouse stock input (no PR/delivery ever,
        // so GetItemStockLevelsAsync never returns them) as zero-movement rows — otherwise a
        // count for a brand-new item stays invisible here despite being the whole point of
        // RAL-193.
        IReadOnlyDictionary<string, decimal> varianceMap = division is null
            ? await _stockBalances.GetAllVarianceTotalsAsync(cancellationToken)
            : new Dictionary<string, decimal>();
        if (varianceMap.Count > 0)
            stockLevels = MergeCountOnlyStockLevels(stockLevels, varianceMap);

        // Only the StockNos already present in stockLevels are ever read from this map —
        // fetch exactly those, never the full catalog.
        IReadOnlyList<ItemMaster> catalog = await _items.GetByStockNosAsync(
            stockLevels.Select(l => l.StockNo).ToList(), cancellationToken);
        Dictionary<string, ItemMaster> catalogMap =
            catalog.ToDictionary(i => i.StockNo, i => i);

        int inStock        = 0;
        int lowOrOutStock  = 0;

        foreach (ItemStockLevel level in stockLevels)
        {
            decimal onHand = level.QtyDelivered - level.QtyDistributed
                + varianceMap.GetValueOrDefault(level.StockNo, 0m);
            int reorderQty = catalogMap.TryGetValue(level.StockNo, out ItemMaster? master)
                ? master.ReorderQty : 0;

            if (onHand > reorderQty)
                inStock++;
            else
                lowOrOutStock++;
        }

        // UniqueItemsTracked — count distinct StockNos with any PR activity (within scope).
        int uniqueItems = stockLevels.Count;

        AlertsGroupDto alertsGroup = new(
            InStock:            inStock,
            LowOrOutOfStock:    lowOrOutStock,
            TotalPRValue:       prStats.TotalAmount,
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
        DivisionScope scope = DivisionScope.Resolve(requester);

        // Office users (Staff with no division) have no inventory scope.
        if (scope.SeeNothing)
            return Array.Empty<ItemLedgerRowDto>();

        int? division = scope.DivisionId;

        IReadOnlyList<ItemStockLevel> stockLevels =
            await _inventory.GetItemStockLevelsAsync(division, cancellationToken);

        bool dateFiltered = deliveryDateFrom.HasValue && deliveryDateTo.HasValue;

        // If a delivery date range is specified, restrict to items that had at
        // least one delivery within that window. Totals remain all-time figures.
        if (dateFiltered)
        {
            IReadOnlySet<string> deliveredStockNos =
                await _inventory.GetStockNosDeliveredInRangeAsync(
                    dateFrom: deliveryDateFrom!.Value,
                    dateTo:   deliveryDateTo!.Value,
                    divisionId: division,
                    cancellationToken: cancellationToken);

            stockLevels = stockLevels
                .Where(l => deliveredStockNos.Contains(l.StockNo))
                .ToList();
        }

        // RAL-193: physical-count variance only applies to the unscoped (Admin/SuperAdmin)
        // view — see class doc. Empty for Staff/Observer, so onHand below is unchanged. When
        // no delivery-date filter is active, also folds in StockNos recorded purely via
        // warehouse stock input (no PR/delivery ever, so GetItemStockLevelsAsync never
        // returns them) as zero-movement rows — otherwise a count for a brand-new item stays
        // invisible here despite being the whole point of RAL-193. Skipped when date-filtered:
        // a "delivered in range" filter can never match an item with no deliveries at all.
        IReadOnlyDictionary<string, decimal> varianceMap;
        if (division is null && !dateFiltered)
        {
            varianceMap = await _stockBalances.GetAllVarianceTotalsAsync(cancellationToken);
            if (varianceMap.Count > 0)
                stockLevels = MergeCountOnlyStockLevels(stockLevels, varianceMap);
        }
        else if (division is null)
        {
            varianceMap = await _stockBalances.GetTotalVarianceByStockNosAsync(
                stockLevels.Select(l => l.StockNo).ToList(), cancellationToken);
        }
        else
        {
            varianceMap = new Dictionary<string, decimal>();
        }

        // Only the StockNos in (the possibly date-filtered/count-only-merged) stockLevels are
        // ever read from this map — fetch exactly those, never the full catalog.
        IReadOnlyList<ItemMaster> catalog = await _items.GetByStockNosAsync(
            stockLevels.Select(l => l.StockNo).ToList(), cancellationToken);
        Dictionary<string, ItemMaster> catalogMap =
            catalog.ToDictionary(i => i.StockNo, i => i);

        List<ItemLedgerRowDto> rows = new(stockLevels.Count);

        foreach (ItemStockLevel level in stockLevels)
        {
            decimal onHand = level.QtyDelivered - level.QtyDistributed
                + varianceMap.GetValueOrDefault(level.StockNo, 0m);

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
    /// Adds a zero-movement <see cref="ItemStockLevel"/> for every StockNo present in
    /// <paramref name="varianceMap"/> but absent from <paramref name="stockLevels"/> — a
    /// StockNo with a warehouse stock input count but no PR/delivery history at all. Its
    /// on-hand then evaluates to exactly its counted quantity (0 movement + variance).
    /// </summary>
    private static IReadOnlyList<ItemStockLevel> MergeCountOnlyStockLevels(
        IReadOnlyList<ItemStockLevel> stockLevels,
        IReadOnlyDictionary<string, decimal> varianceMap)
    {
        HashSet<string> existing = stockLevels
            .Select(l => l.StockNo)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<ItemStockLevel> countOnly = varianceMap.Keys
            .Where(stockNo => !existing.Contains(stockNo))
            .Select(stockNo => new ItemStockLevel(stockNo, QtyOrdered: 0m, QtyDelivered: 0m, QtyDistributed: 0m));

        return stockLevels.Concat(countOnly).ToList();
    }

    /// <summary>
    /// All-zero stats — returned for users whose inventory scope is empty
    /// (e.g. an office user with no division).
    /// </summary>
    private static InventoryStatsDto EmptyStats()
        => new(
            new PRStatsGroupDto(Total: 0, Open: 0, PartiallyDelivered: 0, FullyDeliveredOrCompleted: 0),
            new AlertsGroupDto(InStock: 0, LowOrOutOfStock: 0, TotalPRValue: 0m, UniqueItemsTracked: 0));
}
