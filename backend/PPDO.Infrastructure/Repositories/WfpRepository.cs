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
        int? aipRecordId, int? officeId, CancellationToken ct = default)
    {
        IQueryable<WfpRecord> query = _context.Set<WfpRecord>();
        if (aipRecordId.HasValue) query = query.Where(r => r.AipRecordId == aipRecordId.Value);
        if (officeId.HasValue)    query = query.Where(r => r.OfficeId    == officeId.Value);
        return await query.OrderByDescending(r => r.UpdatedAt).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<WfpRecord?> FindByAipAndOfficeAsync(
        int aipRecordId, int officeId, CancellationToken ct = default)
        => await _context.Set<WfpRecord>()
            .FirstOrDefaultAsync(r => r.AipRecordId == aipRecordId && r.OfficeId == officeId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpActivity>> GetActivitiesByWfpIdAsync(
        int wfpId, CancellationToken ct = default)
        => await _context.Set<WfpActivity>()
            .Where(a => a.WfpId == wfpId)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpExpenditureLine>> GetLinesByActivityIdsAsync(
        IReadOnlyList<int> activityIds, CancellationToken ct = default)
    {
        if (activityIds.Count == 0) return [];
        return await _context.Set<WfpExpenditureLine>()
            .Where(l => activityIds.Contains(l.WfpActivityId))
            .ToListAsync(ct);
    }
}
