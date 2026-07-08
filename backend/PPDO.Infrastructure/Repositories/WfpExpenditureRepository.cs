using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IWfpExpenditureRepository"/> (RAL-120).</summary>
public sealed class WfpExpenditureRepository : Repository<WfpExpenditure>, IWfpExpenditureRepository
{
    public WfpExpenditureRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<WfpExpenditure?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<WfpExpenditure>()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpExpenditurePeriod>> GetPeriodsByExpenditureIdAsync(
        int expenditureId, CancellationToken ct = default)
        => await _context.Set<WfpExpenditurePeriod>()
            .Where(p => p.ExpenditureId == expenditureId)
            .OrderBy(p => p.PeriodNo)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpProcurementItem>> GetProcurementItemsByExpenditureIdAsync(
        int expenditureId, CancellationToken ct = default)
        => await _context.Set<WfpProcurementItem>()
            .Where(i => i.ExpenditureId == expenditureId)
            .OrderBy(i => i.PeriodNo)
            .ToListAsync(ct);
}
