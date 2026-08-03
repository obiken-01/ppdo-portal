using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IStockBalanceRepository"/>.
/// </summary>
public sealed class StockBalanceRepository
    : Repository<StockBalance>, IStockBalanceRepository
{
    public StockBalanceRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalance>> GetByStockNoAsync(
        string stockNo,
        CancellationToken cancellationToken = default)
        => await _context.Set<StockBalance>()
            .Where(b => b.StockNo == stockNo)
            .OrderByDescending(b => b.EffectiveDate)
            .ThenByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, decimal>> GetTotalVarianceByStockNosAsync(
        IReadOnlyCollection<string> stockNos,
        CancellationToken cancellationToken = default)
    {
        if (stockNos.Count == 0) return new Dictionary<string, decimal>();

        List<StockNoVarianceTotal> totals = await _context.Set<StockBalance>()
            .Where(b => stockNos.Contains(b.StockNo))
            .GroupBy(b => b.StockNo)
            .Select(g => new StockNoVarianceTotal(g.Key, g.Sum(b => b.VarianceQty)))
            .ToListAsync(cancellationToken);

        return totals.ToDictionary(t => t.StockNo, t => t.Total);
    }

    /// <inheritdoc />
    public async Task<StockBalance?> FindByStockNoAndEffectiveDateAsync(
        string stockNo,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
        => await _context.Set<StockBalance>()
            .FirstOrDefaultAsync(
                b => b.StockNo == stockNo && b.EffectiveDate == effectiveDate,
                cancellationToken);

    private sealed record StockNoVarianceTotal(string StockNo, decimal Total);
}
