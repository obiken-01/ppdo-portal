using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IPriceIndexItemRepository"/> (RAL-164).</summary>
public sealed class PriceIndexItemRepository : Repository<PriceIndexItem>, IPriceIndexItemRepository
{
    public PriceIndexItemRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexItem>> GetByIdsAsync(
        IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        return await _context.Set<PriceIndexItem>()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexItem>> GetFilteredAsync(
        bool? isActive, string? search, CancellationToken ct = default)
    {
        IQueryable<PriceIndexItem> query = _context.Set<PriceIndexItem>();

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(s) ||
                (p.Category != null && p.Category.Contains(s)) ||
                (p.StockCardNo != null && p.StockCardNo.Contains(s)));
        }

        return await query.OrderBy(p => p.Name).ToListAsync(ct);
    }
}
