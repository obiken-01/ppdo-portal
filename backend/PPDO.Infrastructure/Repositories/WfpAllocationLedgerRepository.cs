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
        int divisionId, int fiscalYear, int wfpRecordId, CancellationToken ct = default)
        => await _context.Set<WfpDivisionAllocationLedger>()
            .FirstOrDefaultAsync(l =>
                l.DivisionId == divisionId && l.FiscalYear == fiscalYear && l.WfpRecordId == wfpRecordId, ct);

    /// <inheritdoc />
    public async Task<decimal> SumUsedAmountAsync(
        int divisionId, int fiscalYear, int? excludeWfpRecordId, CancellationToken ct = default)
    {
        IQueryable<WfpDivisionAllocationLedger> query = _context.Set<WfpDivisionAllocationLedger>()
            .Where(l => l.DivisionId == divisionId && l.FiscalYear == fiscalYear);

        if (excludeWfpRecordId.HasValue)
            query = query.Where(l => l.WfpRecordId != excludeWfpRecordId.Value);

        return await query.SumAsync(l => (decimal?)l.UsedAmount, ct) ?? 0m;
    }
}
