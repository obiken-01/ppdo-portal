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
    public async Task<IReadOnlyList<AipProcurementItem>> GetProcurementItemsByExpenditureIdsAsync(
        IReadOnlyList<int> expenditureIds, CancellationToken ct = default)
    {
        if (expenditureIds.Count == 0) return [];
        return await _context.Set<AipProcurementItem>()
            .Where(i => expenditureIds.Contains(i.ExpenditureId))
            .OrderBy(i => i.ExpenditureId).ThenBy(i => i.Id)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task ReplaceProcurementItemsAsync(
        int expenditureId, IReadOnlyList<AipProcurementItem> items, CancellationToken ct = default)
    {
        List<AipProcurementItem> existing = await _context.Set<AipProcurementItem>()
            .Where(i => i.ExpenditureId == expenditureId)
            .ToListAsync(ct);

        _context.Set<AipProcurementItem>().RemoveRange(existing);

        foreach (AipProcurementItem item in items)
            item.ExpenditureId = expenditureId;

        await _context.Set<AipProcurementItem>().AddRangeAsync(items, ct);
        // No SaveChangesAsync — the calling service owns the unit of work, so the replace commits
        // in the same transaction as the parent line's own change.
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipActivityLineCountDto>> CountByActivityIdsAsync(
        IReadOnlyList<int> activityIds, CancellationToken ct = default)
    {
        if (activityIds.Count == 0) return [];

        return await _context.Set<AipExpenditure>()
            .Where(e => activityIds.Contains(e.ActivityId))
            .GroupBy(e => e.ActivityId)
            .Select(g => new AipActivityLineCountDto(
                g.Key,
                g.Count(),
                g.Count(e => e.FundingSourceId == null)))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipActivityFundCodeDto>> GetFundCodesByAipRecordAsync(
        int aipRecordId, CancellationToken ct = default)
    {
        // GROUP BY (activity, code) across the record's whole subtree — one query for a tree of
        // any size. The per-activity form of this would be one query per row, which on a
        // host-office read is thousands: the N+1 that cost ~60 round trips on the dashboard
        // (RAL-166), an order of magnitude worse.
        //
        // ⚠️ MIN(id) travels with each code because SQL cannot build the joined "GF/GAD Fund"
        // string. The caller joins them, and this is the key that makes that join stable and in
        // entry order — see AipActivityFundCodeDto.
        List<AipActivityFundCodeDto> rows = await _context.Set<AipExpenditure>()
            .Where(e => e.Activity.Project.Program.Office.AipRecordId == aipRecordId
                     && e.FundingSourceSnapshot != null)
            .GroupBy(e => new { e.ActivityId, Code = e.FundingSourceSnapshot! })
            .Select(g => new AipActivityFundCodeDto(
                g.Key.ActivityId, g.Key.Code, g.Min(e => e.Id)))
            .ToListAsync(ct);

        // Ordered after materialising: the sort is over one small projected row set, and ordering
        // a GroupBy by an aggregate is the kind of expression that silently falls back to client
        // evaluation anyway. The filter and the grouping — the parts that touch the table — are
        // both in SQL.
        return rows
            .OrderBy(r => r.ActivityId)
            .ThenBy(r => r.FirstLineId)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetFundCodesByActivityIdAsync(
        int activityId, CancellationToken ct = default)
    {
        List<AipActivityFundCodeDto> rows = await _context.Set<AipExpenditure>()
            .Where(e => e.ActivityId == activityId && e.FundingSourceSnapshot != null)
            .GroupBy(e => e.FundingSourceSnapshot!)
            .Select(g => new AipActivityFundCodeDto(activityId, g.Key, g.Min(e => e.Id)))
            .ToListAsync(ct);

        return rows.OrderBy(r => r.FirstLineId).Select(r => r.Code).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipActivityFundTotalsDto>> SumMooeCoByConfigOfficeAndFundAsync(
        int aipRecordId, int configOfficeId, int fundingSourceId, CancellationToken ct = default)
        // One GROUP BY across the office's whole subtree, joined down office → program → project →
        // activity. The alternative — walk the tree, then one sum per activity — is the N+1 that
        // cost ~60 sequential round trips on the dashboard (RAL-166), and an office's AIP has far
        // more activities than a dashboard has divisions.
        //
        // ⚠️ Matched on the CONFIG office (AipOffice.OfficeId), not on one AipOffice row, and
        // bounded to the record. An office owns one group row per sub-office per sector — PGO has
        // eight in FY2028 — and the ceiling is a single office-level bound over all of them.
        // Scoping this to one row is what let seven of PGO's eight groups encode General Fund
        // money that no ceiling check could see.
        //
        // ⚠️ Filtered to ONE funding source by the caller, which passes General Fund. Non-GF funds
        // are excluded here by an explicit argument rather than by having no ceiling row — a
        // missing allocation resolves to 0m, so absence would silently forbid a fund instead of
        // ignoring it (V18-46 trap 3).
        //
        // ⚠️ Grouped per ACTIVITY and returned un-summed, because the ceiling rounds each figure
        // up to the thousand before adding (DECISION 9). Summing here would round after the sum.
        => await _context.Set<AipExpenditure>()
            .Where(e => e.FundingSourceId == fundingSourceId
                     && e.Activity.Project.Program.Office.AipRecordId == aipRecordId
                     && e.Activity.Project.Program.Office.OfficeId == configOfficeId)
            .GroupBy(e => e.ActivityId)
            .Select(g => new AipActivityFundTotalsDto(
                g.Key,
                g.Sum(e => (decimal?)e.Mooe) ?? 0m,
                g.Sum(e => (decimal?)e.Co) ?? 0m))
            .ToListAsync(ct);
}
