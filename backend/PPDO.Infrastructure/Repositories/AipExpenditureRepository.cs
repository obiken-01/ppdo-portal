using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAipExpenditureRepository"/> (v1.8.0 Phase 2 — V18-33).
/// Every method pushes its WHERE / GROUP BY to SQL; the table is never materialised to filter or
/// sum in memory.
/// </summary>
public sealed class AipExpenditureRepository : Repository<AipExpenditure>, IAipExpenditureRepository
{
    public AipExpenditureRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<AipExpenditure?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipExpenditure>().FirstOrDefaultAsync(e => e.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipExpenditure>> GetByActivityIdAsync(
        int activityId, CancellationToken ct = default)
        => await _context.Set<AipExpenditure>()
            .Where(e => e.ActivityId == activityId)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipExpenditure>> GetByActivityIdsAsync(
        IReadOnlyList<int> activityIds, CancellationToken ct = default)
    {
        if (activityIds.Count == 0) return [];
        return await _context.Set<AipExpenditure>()
            .Where(e => activityIds.Contains(e.ActivityId))
            .OrderBy(e => e.ActivityId).ThenBy(e => e.Id)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipExpenditureTotalsDto> SumByActivityIdAsync(
        int activityId, CancellationToken ct = default)
    {
        // GroupBy over a filtered set, projected to one row. The alternative — four separate
        // SumAsync calls — is four round trips for the same answer, and this runs on every write
        // once V18-34 lands.
        //
        // ⚠️ SUM over zero rows is SQL NULL, so every aggregate is coalesced. Without that, an
        // activity with no lines would be indistinguishable from one never computed, at the one
        // place V18-34 reads to decide whether to touch the parent. That distinction is what keeps
        // the recompute from zeroing FY≤2027 activities, which have no lines at all.
        AipExpenditureTotalsDto? totals = await _context.Set<AipExpenditure>()
            .Where(e => e.ActivityId == activityId)
            .GroupBy(_ => 1)
            .Select(g => new AipExpenditureTotalsDto(
                g.Sum(e => (decimal?)e.Ps) ?? 0m,
                g.Sum(e => (decimal?)e.Mooe) ?? 0m,
                g.Sum(e => (decimal?)e.Co) ?? 0m,
                g.Sum(e => (decimal?)e.Total) ?? 0m,
                g.Count()))
            .FirstOrDefaultAsync(ct);

        // No rows at all means no group, so FirstOrDefault returns null rather than a zero row.
        return totals ?? new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 0);
    }
}
