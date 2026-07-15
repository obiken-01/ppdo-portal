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

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpExpenditure>> GetByWfpActivityIdAsync(
        int wfpActivityId, CancellationToken ct = default)
        => await _context.Set<WfpExpenditure>()
            .Where(e => e.WfpActivityId == wfpActivityId)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<WfpExpenditureContext?> GetActivityContextAsync(
        int wfpActivityId, CancellationToken ct = default)
        => await (
            from act in _context.Set<WfpActivity>()
            join rec in _context.Set<WfpRecord>() on act.WfpId equals rec.Id
            where act.Id == wfpActivityId
            select new WfpExpenditureContext(rec.Id, rec.DivisionId, rec.OfficeId, rec.FiscalYear, act.AipActivityId)
        ).FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<decimal> SumTotalByAipActivityAsync(
        int aipActivityId, int officeId, int fiscalYear,
        int? excludeExpenditureId, CancellationToken ct = default)
    {
        IQueryable<WfpExpenditure> query =
            from e in _context.Set<WfpExpenditure>()
            join act in _context.Set<WfpActivity>() on e.WfpActivityId equals act.Id
            join rec in _context.Set<WfpRecord>() on act.WfpId equals rec.Id
            where act.AipActivityId == aipActivityId
               && rec.OfficeId == officeId
               && rec.FiscalYear == fiscalYear
            select e;

        if (excludeExpenditureId.HasValue)
            query = query.Where(e => e.Id != excludeExpenditureId.Value);

        return await query.SumAsync(e => (decimal?)e.TotalAppropriation, ct) ?? 0m;
    }

    /// <inheritdoc />
    public async Task<decimal> SumTotalByWfpRecordAsync(
        int wfpRecordId, int fundingSourceId, int generalFundId,
        int? excludeExpenditureId, CancellationToken ct = default)
    {
        // Effective fund = FundingSourceId ?? generalFundId — a null-fund expenditure counts
        // toward General Fund specifically, never toward whatever fund happens to be queried.
        IQueryable<WfpExpenditure> query =
            from e in _context.Set<WfpExpenditure>()
            join act in _context.Set<WfpActivity>() on e.WfpActivityId equals act.Id
            where act.WfpId == wfpRecordId
               && (e.FundingSourceId ?? generalFundId) == fundingSourceId
            select e;

        if (excludeExpenditureId.HasValue)
            query = query.Where(e => e.Id != excludeExpenditureId.Value);

        return await query.SumAsync(e => (decimal?)e.TotalAppropriation, ct) ?? 0m;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int?>> GetDistinctFundingSourceIdsByWfpRecordAsync(
        int wfpRecordId, CancellationToken ct = default)
        => await (
            from e in _context.Set<WfpExpenditure>()
            join act in _context.Set<WfpActivity>() on e.WfpActivityId equals act.Id
            where act.WfpId == wfpRecordId
            select e.FundingSourceId
        ).Distinct().ToListAsync(ct);
}
