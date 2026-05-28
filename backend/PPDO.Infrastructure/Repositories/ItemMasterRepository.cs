using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IItemMasterRepository"/>.
/// Provides item-catalog-specific queries on top of the generic <see cref="Repository{T}"/> base.
///
/// All queries are async.
/// </summary>
public sealed class ItemMasterRepository
    : Repository<ItemMaster>, IItemMasterRepository
{
    public ItemMasterRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<ItemMaster?> GetByStockNoAsync(
        string stockNo,
        CancellationToken cancellationToken = default)
        => await _context.ItemMasters
            .FirstOrDefaultAsync(i => i.StockNo == stockNo, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMaster>> GetNewItemsAsync(
        CancellationToken cancellationToken = default)
        => await _context.ItemMasters
            .Where(i => i.IsNewItem)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMaster>> SearchAsync(
        string term,
        CancellationToken cancellationToken = default)
        => await _context.ItemMasters
            .Where(i => i.StockNo.Contains(term) || i.Description.Contains(term))
            .OrderBy(i => i.StockNo)
            .Take(20)
            .ToListAsync(cancellationToken);
}
