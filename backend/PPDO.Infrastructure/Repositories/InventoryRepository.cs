using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IInventoryRepository"/>.
/// Uses SQL-level GROUP BY via LINQ query syntax to aggregate across
/// PRItems, DeliveryItems, and Distributions without loading full entity graphs.
/// Three separate queries are executed and merged in memory — avoids multi-level
/// Include chains and keeps individual query complexity low.
/// </summary>
public sealed class InventoryRepository : IInventoryRepository
{
    private readonly AppDbContext _context;

    public InventoryRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemStockLevel>> GetItemStockLevelsAsync(
        int? divisionId,
        CancellationToken cancellationToken = default)
    {
        // ── QtyOrdered per StockNo ────────────────────────────────────────────
        List<(string StockNo, decimal QtyOrdered)> orderedRows =
            await (from pi in _context.PRItems
                   join pr in _context.PurchaseRequests on pi.PRId equals pr.Id
                   where (divisionId == null || pr.DivisionId == divisionId)
                      && pi.StockNo != null && pi.StockNo != ""
                   group pi by pi.StockNo into g
                   select new ValueTuple<string, decimal>(g.Key!, g.Sum(x => x.Quantity)))
                  .ToListAsync(cancellationToken);

        // ── QtyDelivered per StockNo ──────────────────────────────────────────
        List<(string StockNo, decimal QtyDelivered)> deliveredRows =
            await (from di in _context.DeliveryItems
                   join pi in _context.PRItems on di.PRItemId equals pi.Id
                   join pr in _context.PurchaseRequests on pi.PRId equals pr.Id
                   where (divisionId == null || pr.DivisionId == divisionId)
                      && pi.StockNo != null && pi.StockNo != ""
                   group di by pi.StockNo into g
                   select new ValueTuple<string, decimal>(g.Key!, g.Sum(x => x.QtyDelivered)))
                  .ToListAsync(cancellationToken);

        // ── QtyDistributed per StockNo ────────────────────────────────────────
        // Inner join on the nullable FK naturally excludes warehouse-count-sourced
        // distributions (DeliveryItemId null, RAL-223) — they never touched a DeliveryItem,
        // so they don't belong in this delivery-batch-derived total. Their effect on on-hand
        // is instead netted directly into StockBalanceRepository's variance totals.
        List<(string StockNo, decimal QtyDistributed)> distributedRows =
            await (from dist in _context.Distributions
                   join di in _context.DeliveryItems on dist.DeliveryItemId equals (Guid?)di.Id
                   join pi in _context.PRItems on di.PRItemId equals pi.Id
                   join pr in _context.PurchaseRequests on pi.PRId equals pr.Id
                   where (divisionId == null || pr.DivisionId == divisionId)
                      && pi.StockNo != null && pi.StockNo != ""
                   group dist by pi.StockNo into g
                   select new ValueTuple<string, decimal>(g.Key!, g.Sum(x => x.QtyIssued)))
                  .ToListAsync(cancellationToken);

        // ── Merge in memory ───────────────────────────────────────────────────
        Dictionary<string, decimal> deliveredMap    = deliveredRows.ToDictionary(r => r.Item1, r => r.Item2);
        Dictionary<string, decimal> distributedMap  = distributedRows.ToDictionary(r => r.Item1, r => r.Item2);

        return orderedRows
            .Select(r =>
            {
                deliveredMap.TryGetValue(r.Item1, out decimal delivered);
                distributedMap.TryGetValue(r.Item1, out decimal distributed);
                return new ItemStockLevel(r.Item1, r.Item2, delivered, distributed);
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetStockNosDeliveredInRangeAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int? divisionId = null,
        CancellationToken cancellationToken = default)
    {
        List<string> stockNos =
            await (from d  in _context.Deliveries
                   join di in _context.DeliveryItems        on d.Id        equals di.DeliveryId
                   join pi in _context.PRItems              on di.PRItemId equals pi.Id
                   join pr in _context.PurchaseRequests     on pi.PRId     equals pr.Id
                   where d.DeliveryDate >= dateFrom
                      && d.DeliveryDate <= dateTo
                      && (divisionId == null || pr.DivisionId == divisionId)
                      && pi.StockNo != null && pi.StockNo != ""
                   select pi.StockNo!)
                  .Distinct()
                  .ToListAsync(cancellationToken);

        return new HashSet<string>(stockNos, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<ItemStockLevel> GetItemStockLevelAsync(
        string stockNo,
        CancellationToken cancellationToken = default)
    {
        // Sequential queries on the shared DbContext — never Task.WhenAll (see CLAUDE.md).
        decimal ordered = await _context.PRItems
            .Where(pi => pi.StockNo == stockNo)
            .SumAsync(pi => pi.Quantity, cancellationToken);

        decimal delivered = await (from di in _context.DeliveryItems
                                    join pi in _context.PRItems on di.PRItemId equals pi.Id
                                    where pi.StockNo == stockNo
                                    select di.QtyDelivered)
                                   .SumAsync(cancellationToken);

        // Inner join excludes warehouse-count-sourced distributions (DeliveryItemId null,
        // RAL-223) — same reasoning as GetItemStockLevelsAsync above.
        decimal distributed = await (from dist in _context.Distributions
                                      join di in _context.DeliveryItems on dist.DeliveryItemId equals (Guid?)di.Id
                                      join pi in _context.PRItems on di.PRItemId equals pi.Id
                                      where pi.StockNo == stockNo
                                      select dist.QtyIssued)
                                     .SumAsync(cancellationToken);

        return new ItemStockLevel(stockNo, ordered, delivered, distributed);
    }
}
