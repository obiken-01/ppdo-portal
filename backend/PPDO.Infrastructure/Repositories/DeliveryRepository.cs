using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
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
}
