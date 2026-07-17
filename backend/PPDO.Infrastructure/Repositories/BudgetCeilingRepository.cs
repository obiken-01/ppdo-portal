using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IBudgetCeilingRepository"/> (RAL-163).</summary>
public sealed class BudgetCeilingRepository : Repository<BudgetCeiling>, IBudgetCeilingRepository
{
    public BudgetCeilingRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<BudgetCeiling?> FindAsync(
        int officeId, int fiscalYear, int fundingSourceId, CancellationToken ct = default)
        => await _context.Set<BudgetCeiling>()
            .FirstOrDefaultAsync(c =>
                c.OfficeId == officeId && c.FiscalYear == fiscalYear && c.FundingSourceId == fundingSourceId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetCeiling>> GetByOfficeAndFiscalYearAsync(
        int officeId, int fiscalYear, CancellationToken ct = default)
        => await _context.Set<BudgetCeiling>()
            .Where(c => c.OfficeId == officeId && c.FiscalYear == fiscalYear)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetCeiling>> GetByFiscalYearAndFundingSourceAsync(
        int fiscalYear, int fundingSourceId, CancellationToken ct = default)
        => await _context.Set<BudgetCeiling>()
            .Where(c => c.FiscalYear == fiscalYear && c.FundingSourceId == fundingSourceId)
            .ToListAsync(ct);
}
