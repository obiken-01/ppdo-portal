using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IAipAllocationLedgerRepository"/> (V18-45 / PPDO-55).</summary>
public sealed class AipAllocationLedgerRepository : Repository<AipDivisionAllocationLedger>, IAipAllocationLedgerRepository
{
    public AipAllocationLedgerRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<AipDivisionAllocationLedger?> FindAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int aipActivityId, CancellationToken ct = default)
        => await _context.Set<AipDivisionAllocationLedger>()
            .FirstOrDefaultAsync(l =>
                l.DivisionId == divisionId && l.FiscalYear == fiscalYear
                && l.FundingSourceId == fundingSourceId && l.AipActivityId == aipActivityId, ct);

    /// <inheritdoc />
    public async Task<decimal> SumReservedAmountAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int? excludeAipActivityId, CancellationToken ct = default)
    {
        IQueryable<AipDivisionAllocationLedger> query = _context.Set<AipDivisionAllocationLedger>()
            .Where(l => l.DivisionId == divisionId && l.FiscalYear == fiscalYear
                     && l.FundingSourceId == fundingSourceId);

        if (excludeAipActivityId.HasValue)
            query = query.Where(l => l.AipActivityId != excludeAipActivityId.Value);

        // No Math.Max here or anywhere above it — a negative remaining is the point.
        return await query.SumAsync(l => (decimal?)l.ReservedAmount, ct) ?? 0m;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetFundingSourceIdsForActivityAsync(
        int aipActivityId, CancellationToken ct = default)
        => await _context.Set<AipDivisionAllocationLedger>()
            .Where(l => l.AipActivityId == aipActivityId)
            .Select(l => l.FundingSourceId)
            .Distinct()
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionFundReservedAmountDto>> SumReservedAmountsByDivisionsAsync(
        IReadOnlyList<int> divisionIds, int fiscalYear, CancellationToken ct = default)
        => await _context.Set<AipDivisionAllocationLedger>()
            .Where(l => divisionIds.Contains(l.DivisionId) && l.FiscalYear == fiscalYear)
            .GroupBy(l => new { l.DivisionId, l.FundingSourceId })
            .Select(g => new DivisionFundReservedAmountDto(
                g.Key.DivisionId, g.Key.FundingSourceId, g.Sum(l => l.ReservedAmount)))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<int> DeleteByActivityAsync(int aipActivityId, CancellationToken ct = default)
        => await _context.Set<AipDivisionAllocationLedger>()
            .Where(l => l.AipActivityId == aipActivityId)
            .ExecuteDeleteAsync(ct);
}
