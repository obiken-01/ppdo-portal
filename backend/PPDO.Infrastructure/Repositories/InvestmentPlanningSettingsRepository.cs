using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IInvestmentPlanningSettingsRepository"/> (PPDO-136).</summary>
public sealed class InvestmentPlanningSettingsRepository : IInvestmentPlanningSettingsRepository
{
    private readonly AppDbContext _context;

    public InvestmentPlanningSettingsRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<InvestmentPlanningSettings?> GetAsync(CancellationToken ct = default)
        => await _context.Set<InvestmentPlanningSettings>()
            .Include(s => s.UpdatedBy)
            .FirstOrDefaultAsync(s => s.Id == InvestmentPlanningSettings.SingletonId, ct);

    /// <inheritdoc />
    public async Task<int?> GetDefaultFiscalYearAsync(CancellationToken ct = default)
        => await _context.Set<InvestmentPlanningSettings>()
            .AsNoTracking()
            .Where(s => s.Id == InvestmentPlanningSettings.SingletonId)
            .Select(s => s.DefaultFiscalYear)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await _context.SaveChangesAsync(ct);
}
