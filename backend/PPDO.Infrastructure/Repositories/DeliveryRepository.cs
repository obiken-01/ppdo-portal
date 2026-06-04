using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IDeliveryRepository"/>.
///
/// Include depth never exceeds 2 levels per the project rules in CLAUDE.md.
/// All queries are async.
/// </summary>
public sealed class DeliveryRepository : Repository<Delivery>, IDeliveryRepository
{
    public DeliveryRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Delivery>> GetByPRIdAsync(
        Guid prId,
        CancellationToken cancellationToken = default)
        => await _context.Deliveries
            .Where(d => d.PRId == prId)
            .OrderByDescending(d => d.DeliveryDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Delivery?> GetByIdWithItemsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _context.Deliveries
            .Include(d => d.Items)                      // depth 1
                .ThenInclude(di => di.Distributions)   // depth 2
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<Dictionary<Guid, decimal>> GetTotalDeliveredByPRAsync(
        Guid prId,
        CancellationToken cancellationToken = default)
    {
        // Join DeliveryItems → Deliveries filtered by PRId, group by PRItemId.
        List<(Guid PRItemId, decimal TotalQty)> rows = await _context.DeliveryItems
            .Where(di => di.Delivery!.PRId == prId)
            .GroupBy(di => di.PRItemId)
            .Select(g => new ValueTuple<Guid, decimal>(g.Key, g.Sum(di => di.QtyDelivered)))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Item1, r => r.Item2);
    }

    /// <inheritdoc />
    public async Task<bool> DeliveryRefExistsAsync(
        string deliveryRef,
        CancellationToken cancellationToken = default)
        => await _context.Deliveries
            .AnyAsync(d => d.DeliveryRef == deliveryRef, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Delivery>> GetDeliveriesForPRReportAsync(
        Guid prId,
        CancellationToken cancellationToken = default)
        => await _context.Deliveries
            .Where(d => d.PRId == prId)
            .Include(d => d.Items)                      // depth 1
                .ThenInclude(di => di.Distributions)   // depth 2 — sibling A
            .Include(d => d.Items)                      // depth 1
                .ThenInclude(di => di.PRItem)           // depth 2 — sibling B
            .OrderBy(d => d.DeliveryDate)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeliveryItemBreakdownRow>> GetDeliveryItemBreakdownsByStockNoAsync(
        string stockNo,
        Division? division = null,
        CancellationToken cancellationToken = default)
    {
        // Load all DeliveryItems whose PRItem.StockNo matches, with full context.
        List<DeliveryItem> items = await _context.DeliveryItems
            .Include(di => di.Distributions)
            .Include(di => di.Delivery)
            .Include(di => di.PRItem)
                .ThenInclude(pi => pi!.PurchaseRequest)
            .Where(di => di.PRItem!.StockNo == stockNo
                && (division == null || di.PRItem.PurchaseRequest!.Division == division))
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(di => di.Delivery?.DeliveryDate ?? DateOnly.MinValue)
            .Select(di => new DeliveryItemBreakdownRow(
                DeliveryItemId: di.Id,
                DeliveryRef:    di.Delivery?.DeliveryRef    ?? "—",
                DeliveryDate:   di.Delivery?.DeliveryDate   ?? DateOnly.MinValue,
                PRId:           di.PRItem?.PRId             ?? Guid.Empty,
                PRNo:           di.PRItem?.PurchaseRequest?.PRNo ?? "—",
                QtyDelivered:   di.QtyDelivered,
                Distributions:  di.Distributions
                    .Select(dist => new DistributionBreakdownRow(
                        dist.Id, dist.IssueRef, dist.Division,
                        dist.QtyIssued, dist.DateIssued,
                        dist.IssuedBy, dist.Remarks))
                    .ToList()))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<DeliveryItemBreakdownRow?> GetDeliveryItemBreakdownAsync(
        Guid deliveryItemId,
        CancellationToken cancellationToken = default)
    {
        DeliveryItem? di = await _context.DeliveryItems
            .Include(x => x.Distributions)
            .Include(x => x.PRItem)
                .ThenInclude(pi => pi!.PurchaseRequest)
            .Include(x => x.Delivery)
            .FirstOrDefaultAsync(x => x.Id == deliveryItemId, cancellationToken);

        if (di is null) return null;

        return new DeliveryItemBreakdownRow(
            DeliveryItemId: di.Id,
            DeliveryRef:    di.Delivery?.DeliveryRef    ?? "—",
            DeliveryDate:   di.Delivery?.DeliveryDate   ?? DateOnly.MinValue,
            PRId:           di.PRItem?.PRId             ?? Guid.Empty,
            PRNo:           di.PRItem?.PurchaseRequest?.PRNo ?? "—",
            QtyDelivered:   di.QtyDelivered,
            Distributions:  di.Distributions
                .Select(dist => new DistributionBreakdownRow(
                    dist.Id, dist.IssueRef, dist.Division,
                    dist.QtyIssued, dist.DateIssued,
                    dist.IssuedBy, dist.Remarks))
                .ToList());
    }
}
