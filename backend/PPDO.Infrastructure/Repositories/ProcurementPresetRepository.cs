using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IProcurementPresetRepository"/> (RAL-119).</summary>
public sealed class ProcurementPresetRepository : Repository<ProcurementPreset>, IProcurementPresetRepository
{
    public ProcurementPresetRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<ProcurementPreset?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<ProcurementPreset>()
            .Include(p => p.Items)
            .Include(p => p.CreatedBy)
            .Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcurementPreset>> GetByAccountIdAsync(int accountId, CancellationToken ct = default)
        => await _context.Set<ProcurementPreset>()
            .Include(p => p.Items)
            .Include(p => p.CreatedBy)
            .Include(p => p.Account)
            .Where(p => p.AccountId == accountId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcurementPreset>> GetAllWithItemsAsync(CancellationToken ct = default)
        => await _context.Set<ProcurementPreset>()
            .Include(p => p.Items)
            .Include(p => p.CreatedBy)
            .Include(p => p.Account)
            .OrderBy(p => p.Account.AccountNumber)
            .ThenBy(p => p.Name)
            .ToListAsync(ct);
}
