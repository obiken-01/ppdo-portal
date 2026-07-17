using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ILdipRepository"/> (RAL-61).
/// List/detail reads apply their WHERE filters in SQL and load the Office
/// navigation with a single JOIN. Sector groups load their programs with one
/// Include (offices → programs, within the 2-level limit).
/// </summary>
public sealed class LdipRepository : Repository<LdipRecord>, ILdipRepository
{
    public LdipRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<LdipRecord?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<LdipRecord>()
            .Include(r => r.Office)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LdipRecord>> GetListAsync(
        int? officeId, string? status, CancellationToken ct = default)
    {
        IQueryable<LdipRecord> q = _context.Set<LdipRecord>().Include(r => r.Office);
        if (officeId is not null)
            q = q.Where(r => r.OfficeId == officeId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            string s = status.Trim();
            q = q.Where(r => r.Status == s);
        }
        return await q.OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LdipOffice>> GetOfficeGroupsAsync(
        int ldipRecordId, CancellationToken ct = default)
        => await _context.Set<LdipOffice>()
            .Where(o => o.LdipRecordId == ldipRecordId)
            .Include(o => o.Programs.OrderBy(p => p.RefCode))
            .OrderBy(o => o.RefCode)
            .ToListAsync(ct);

    /// <inheritdoc />
    public Task DeleteOfficeGroupAsync(LdipOffice group, CancellationToken ct = default)
    {
        _context.Set<LdipOffice>().Remove(group);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddOfficeGroupAsync(LdipOffice group, CancellationToken ct = default)
        => _context.Set<LdipOffice>().AddAsync(group, ct).AsTask();

    /// <inheritdoc />
    public async Task<int> CountByFiscalYearStartAsync(int fiscalYearStart, CancellationToken ct = default)
        => await _context.Set<LdipRecord>()
            .CountAsync(r => r.FiscalYearStart == fiscalYearStart, ct);
}
