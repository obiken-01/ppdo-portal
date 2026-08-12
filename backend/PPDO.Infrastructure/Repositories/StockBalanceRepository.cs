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

        List<StockNoVarianceTotal> distributed = await _context.Set<Distribution>()
            .Where(d => d.StockBalanceId != null && stockNos.Contains(d.StockBalance!.StockNo))
            .GroupBy(d => d.StockBalance!.StockNo)
            .Select(g => new StockNoVarianceTotal(g.Key, g.Sum(d => d.QtyIssued)))
            .ToListAsync(cancellationToken);

        return NetOut(totals, distributed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, decimal>> GetAllVarianceTotalsAsync(
        CancellationToken cancellationToken = default)
    {
        List<StockNoVarianceTotal> totals = await _context.Set<StockBalance>()
            .GroupBy(b => b.StockNo)
            .Select(g => new StockNoVarianceTotal(g.Key, g.Sum(b => b.VarianceQty)))
            .ToListAsync(cancellationToken);

        List<StockNoVarianceTotal> distributed = await _context.Set<Distribution>()
            .Where(d => d.StockBalanceId != null)
            .GroupBy(d => d.StockBalance!.StockNo)
            .Select(g => new StockNoVarianceTotal(g.Key, g.Sum(d => d.QtyIssued)))
            .ToListAsync(cancellationToken);

        return NetOut(totals, distributed);
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

    /// <inheritdoc />
    public async Task<WarehouseCountPoolRow?> GetPoolByStockNoAsync(
        string stockNo,
        CancellationToken cancellationToken = default)
    {
        // Sequential queries on the shared DbContext — never Task.WhenAll (see CLAUDE.md).
        StockBalance? latest = await _context.Set<StockBalance>()
            .Where(b => b.StockNo == stockNo)
            .OrderByDescending(b => b.EffectiveDate)
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null) return null;

        decimal grossVariance = await _context.Set<StockBalance>()
            .Where(b => b.StockNo == stockNo)
            .SumAsync(b => b.VarianceQty, cancellationToken);

        decimal totalDistributed = await _context.Set<Distribution>()
            .Where(d => d.StockBalanceId != null && d.StockBalance!.StockNo == stockNo)
            .SumAsync(d => (decimal?)d.QtyIssued, cancellationToken) ?? 0m;

        return new WarehouseCountPoolRow(latest.Id, latest.EffectiveDate, grossVariance, totalDistributed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DistributionBreakdownRow>> GetDistributionsByStockNoAsync(
        string stockNo,
        CancellationToken cancellationToken = default)
        => await _context.Set<Distribution>()
            .Where(d => d.StockBalanceId != null && d.StockBalance!.StockNo == stockNo)
            .OrderBy(d => d.DateIssued)
            .Select(d => new DistributionBreakdownRow(
                d.Id, d.IssueRef, d.DivisionId, d.QtyIssued, d.DateIssued, d.IssuedBy, d.Remarks))
            .ToListAsync(cancellationToken);

    /// <summary>Subtracts <paramref name="distributed"/> totals from <paramref name="variance"/> totals per StockNo.</summary>
    private static Dictionary<string, decimal> NetOut(
        List<StockNoVarianceTotal> variance, List<StockNoVarianceTotal> distributed)
    {
        Dictionary<string, decimal> net = variance.ToDictionary(t => t.StockNo, t => t.Total);
        foreach (StockNoVarianceTotal d in distributed)
            net[d.StockNo] = net.GetValueOrDefault(d.StockNo, 0m) - d.Total;
        return net;
    }

    private sealed record StockNoVarianceTotal(string StockNo, decimal Total);
}
