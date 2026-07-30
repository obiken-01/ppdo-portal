using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IWfpRepository"/>.
/// Read methods for child entities (WfpActivity, WfpExpenditureLine) are hosted here
/// so the service no longer needs generic repos for those types, and all reads
/// push their WHERE / IN filters to SQL.
/// </summary>
public sealed class WfpRepository : Repository<WfpRecord>, IWfpRepository
{
    public WfpRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<WfpRecord?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<WfpRecord>()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpRecord>> GetFilteredAsync(
        int? aipRecordId, int? officeId, int? divisionId = null, CancellationToken ct = default)
    {
        IQueryable<WfpRecord> query = _context.Set<WfpRecord>();
        if (aipRecordId.HasValue) query = query.Where(r => r.AipRecordId == aipRecordId.Value);
        if (officeId.HasValue)    query = query.Where(r => r.OfficeId    == officeId.Value);
        if (divisionId.HasValue)  query = query.Where(r => r.DivisionId  == divisionId.Value);
        return await query.OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<WfpRecord?> FindByAipOfficeAndDivisionAsync(
        int aipRecordId, int officeId, int? divisionId, CancellationToken ct = default)
    {
        IQueryable<WfpRecord> query = _context.Set<WfpRecord>()
            .Where(r => r.AipRecordId == aipRecordId && r.OfficeId == officeId);
        query = divisionId.HasValue
            ? query.Where(r => r.DivisionId == divisionId.Value)
            : query.Where(r => r.DivisionId == null);
        return await query.FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<WfpRecord?> FindByOfficeDivisionFiscalYearAsync(
        int officeId, int? divisionId, int fiscalYear, CancellationToken ct = default)
    {
        IQueryable<WfpRecord> query = _context.Set<WfpRecord>()
            .Where(r => r.OfficeId == officeId && r.FiscalYear == fiscalYear);
        query = divisionId.HasValue
            ? query.Where(r => r.DivisionId == divisionId.Value)
            : query.Where(r => r.DivisionId == null);
        return await query.FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpActivity>> GetActivitiesByWfpIdAsync(
        int wfpId, CancellationToken ct = default)
        => await _context.Set<WfpActivity>()
            .Where(a => a.WfpId == wfpId)
            .OrderBy(a => a.AipActivityId)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpActivity>> GetActivitiesByWfpIdsAsync(
        IReadOnlyList<int> wfpIds, CancellationToken ct = default)
    {
        if (wfpIds.Count == 0) return [];
        return await _context.Set<WfpActivity>()
            .Where(a => wfpIds.Contains(a.WfpId))
            .OrderBy(a => a.WfpId)
            .ThenBy(a => a.AipActivityId)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpExpenditureLine>> GetLinesByActivityIdsAsync(
        IReadOnlyList<int> activityIds, CancellationToken ct = default)
    {
        if (activityIds.Count == 0) return [];
        return await _context.Set<WfpExpenditureLine>()
            .Where(l => activityIds.Contains(l.WfpActivityId))
            .OrderBy(l => l.WfpActivityId)
            .ThenBy(l => l.SortOrder)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> AnyForAipRecordAsync(int aipRecordId, CancellationToken ct = default)
        => await _context.Set<WfpRecord>().AnyAsync(r => r.AipRecordId == aipRecordId, ct);
}
