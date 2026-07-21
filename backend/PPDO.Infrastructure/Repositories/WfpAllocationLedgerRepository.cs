using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IWfpAllocationLedgerRepository"/> (RAL-122).</summary>
public sealed class WfpAllocationLedgerRepository : Repository<WfpDivisionAllocationLedger>, IWfpAllocationLedgerRepository
{
    public WfpAllocationLedgerRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<WfpDivisionAllocationLedger?> FindAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int wfpRecordId, CancellationToken ct = default)
        => await _context.Set<WfpDivisionAllocationLedger>()
            .FirstOrDefaultAsync(l =>
                l.DivisionId == divisionId && l.FiscalYear == fiscalYear
                && l.FundingSourceId == fundingSourceId && l.WfpRecordId == wfpRecordId, ct);

    /// <inheritdoc />
    public async Task<decimal> SumUsedAmountAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int? excludeWfpRecordId, CancellationToken ct = default)
    {
        IQueryable<WfpDivisionAllocationLedger> query = _context.Set<WfpDivisionAllocationLedger>()
            .Where(l => l.DivisionId == divisionId && l.FiscalYear == fiscalYear
                     && l.FundingSourceId == fundingSourceId);

        if (excludeWfpRecordId.HasValue)
            query = query.Where(l => l.WfpRecordId != excludeWfpRecordId.Value);

        return await query.SumAsync(l => (decimal?)l.UsedAmount, ct) ?? 0m;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetFundingSourceIdsForRecordAsync(
        int wfpRecordId, CancellationToken ct = default)
        => await _context.Set<WfpDivisionAllocationLedger>()
            .Where(l => l.WfpRecordId == wfpRecordId)
            .Select(l => l.FundingSourceId)
            .Distinct()
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionFundUsedAmountDto>> SumUsedAmountsByDivisionsAsync(
        IReadOnlyList<int> divisionIds, int fiscalYear, CancellationToken ct = default)
        => await _context.Set<WfpDivisionAllocationLedger>()
            .Where(l => divisionIds.Contains(l.DivisionId) && l.FiscalYear == fiscalYear)
            .GroupBy(l => new { l.DivisionId, l.FundingSourceId })
            .Select(g => new DivisionFundUsedAmountDto(g.Key.DivisionId, g.Key.FundingSourceId, g.Sum(l => l.UsedAmount)))
            .ToListAsync(ct);
}
