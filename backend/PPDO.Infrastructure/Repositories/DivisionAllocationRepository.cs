using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IDivisionAllocationRepository"/> (RAL-163).</summary>
public sealed class DivisionAllocationRepository : Repository<DivisionAllocation>, IDivisionAllocationRepository
{
    public DivisionAllocationRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionAllocation>> GetByDivisionIdsAsync(
        IReadOnlyList<int> divisionIds, int fiscalYear, int fundingSourceId, CancellationToken ct = default)
    {
        if (divisionIds.Count == 0) return [];
        return await _context.Set<DivisionAllocation>()
            .Where(a => divisionIds.Contains(a.DivisionId)
                     && a.FiscalYear == fiscalYear && a.FundingSourceId == fundingSourceId)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionAllocation>> GetByDivisionIdsAsync(
        IReadOnlyList<int> divisionIds, int fiscalYear, CancellationToken ct = default)
    {
        if (divisionIds.Count == 0) return [];
        return await _context.Set<DivisionAllocation>()
            .Where(a => divisionIds.Contains(a.DivisionId) && a.FiscalYear == fiscalYear)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> HasPositiveAllocationAsync(
        int officeId, int divisionId, int fiscalYear, int fundingSourceId, CancellationToken ct = default)
        // Joined rather than resolved in the service: the office's divisions are not otherwise
        // needed by the one caller, so fetching them to filter in memory would add a full-table
        // read to answer a single EXISTS (PERFORMANCE_GUIDELINES.md).
        => await (from a in _context.Set<DivisionAllocation>()
                  join d in _context.Set<Division>() on a.DivisionId equals d.Id
                  where a.DivisionId == divisionId
                     && a.FiscalYear == fiscalYear
                     && a.FundingSourceId == fundingSourceId
                     && a.Amount > 0
                     && d.OfficeId == officeId
                  select a.Id)
            .AnyAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionAllocation>> GetByFiscalYearAndFundingSourceAsync(
        int fiscalYear, int fundingSourceId, CancellationToken ct = default)
        => await _context.Set<DivisionAllocation>()
            .Where(a => a.FiscalYear == fiscalYear && a.FundingSourceId == fundingSourceId)
            .ToListAsync(ct);
}
