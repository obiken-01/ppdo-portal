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
        List<(string StockNo, decimal QtyDistributed)> distributedRows =
            await (from dist in _context.Distributions
                   join di in _context.DeliveryItems on dist.DeliveryItemId equals di.Id
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
}
