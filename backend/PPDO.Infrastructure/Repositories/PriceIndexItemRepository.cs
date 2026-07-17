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
}
