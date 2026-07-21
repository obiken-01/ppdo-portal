using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAipRepository"/>.
/// Each hierarchy read method applies its WHERE / IN filter in SQL so only
/// the relevant rows are transferred — not the entire table.
/// All four hierarchy tables are accessed via <c>_context.Set&lt;T&gt;()</c>
/// which is safe because <c>_context</c> is the shared scoped DbContext.
/// </summary>
public sealed class AipRepository : Repository<AipRecord>, IAipRepository
{
    public AipRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<AipRecord?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipRecord>()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdAsync(
        int aipRecordId, CancellationToken ct = default)
        => await _context.Set<AipOffice>()
            .Where(o => o.AipRecordId == aipRecordId)
            .OrderBy(o => o.RefCode)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<AipOffice?> GetOfficeByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipOffice>().FirstOrDefaultAsync(o => o.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdsAsync(
        IReadOnlyList<int> aipIds, CancellationToken ct = default)
    {
        if (aipIds.Count == 0) return [];
        return await _context.Set<AipOffice>()
            .Where(o => aipIds.Contains(o.AipRecordId))
            .OrderBy(o => o.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipProgram>> GetProgramsByOfficeIdsAsync(
        IReadOnlyList<int> officeIds, CancellationToken ct = default)
    {
        if (officeIds.Count == 0) return [];
        return await _context.Set<AipProgram>()
            .Where(p => officeIds.Contains(p.OfficeId))
            .OrderBy(p => p.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipProgram?> GetProgramByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipProgram>().FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipProject>> GetProjectsByProgramIdsAsync(
        IReadOnlyList<int> programIds, CancellationToken ct = default)
    {
        if (programIds.Count == 0) return [];
        return await _context.Set<AipProject>()
            .Where(j => programIds.Contains(j.ProgramId))
            .OrderBy(j => j.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipProject?> GetProjectByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipProject>().FirstOrDefaultAsync(j => j.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipActivity>> GetActivitiesByProjectIdsAsync(
        IReadOnlyList<int> projectIds, CancellationToken ct = default)
    {
        if (projectIds.Count == 0) return [];
        return await _context.Set<AipActivity>()
            .Where(a => projectIds.Contains(a.ProjectId))
            .OrderBy(a => a.RefCode)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<AipActivity?> GetActivityByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipActivity>().FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public async Task<AipRecord?> GetLatestByFiscalYearAsync(int fiscalYear, CancellationToken ct = default)
        => await _context.Set<AipRecord>()
            .Where(r => r.FiscalYear == fiscalYear && r.Status != PlanningStatus.Archived)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetDistinctFiscalYearsAsync(CancellationToken ct = default)
        => await _context.Set<AipRecord>()
            .Select(r => r.FiscalYear)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync(ct);
}
